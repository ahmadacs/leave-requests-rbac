import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { toObservable } from '@angular/core/rxjs-interop';
import {
  Observable,
  catchError,
  finalize,
  map,
  shareReplay,
  switchMap,
  tap,
  throwError,
} from 'rxjs';
import { AuthResponse, Me, UserProfile } from '../models';

/**
 * Session state. The access token lives **in memory only** (a signal), never
 * in localStorage. On reload the session is restored from the refresh cookie
 * by the app initializer.
 *
 * Roles come from the login response; effective permissions are always fetched
 * from GET /api/me so a permission change is reflected without re-login.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);

  private readonly accessToken = signal<string | null>(null);

  readonly user = signal<UserProfile | null>(null);
  readonly permissions = signal<readonly string[]>([]);

  readonly isAuthenticated = computed(() => this.user() !== null);
  readonly user$ = toObservable(this.user);
  readonly permissions$ = toObservable(this.permissions);

  readonly currentToken = this.accessToken.asReadonly();

  private refreshInFlight: Observable<AuthResponse> | null = null;

  login(email: string, password: string): Observable<void> {
    return this.http
      .post<AuthResponse>('/api/auth/login', { email, password }, { withCredentials: true })
      .pipe(
        tap((response) => this.applyToken(response)),
        switchMap(() => this.reloadProfile()),
        map(() => void 0),
      );
  }

  /**
   * Rotates the refresh token and reloads /api/me. Concurrent callers share the
   * same in-flight request so parallel 401s trigger exactly one refresh.
   */
  refresh(): Observable<AuthResponse> {
    this.refreshInFlight ??= this.http
      .post<AuthResponse>('/api/auth/refresh', {}, { withCredentials: true })
      .pipe(
        tap((response) => this.applyToken(response)),
        switchMap((response) => this.reloadProfile().pipe(map(() => response))),
        catchError((error: unknown) => {
          this.clearSession();
          return throwError(() => error);
        }),
        finalize(() => {
          this.refreshInFlight = null;
        }),
        shareReplay({ bufferSize: 1, refCount: false }),
      );

    return this.refreshInFlight;
  }

  /** Called by the app initializer on startup. Never throws. */
  restoreSession(): Observable<boolean> {
    return this.refresh().pipe(
      map(() => true),
      catchError(() => {
        this.clearSession();
        return [false];
      }),
    );
  }

  /** Re-reads GET /api/me so the UI picks up permission changes immediately. */
  reloadProfile(): Observable<Me> {
    return this.http.get<Me>('/api/me').pipe(
      tap((me) => {
        this.user.set(me);
        this.permissions.set(me.permissions);
      }),
    );
  }

  logout(): Observable<unknown> {
    return this.http.post('/api/auth/logout', {}, { withCredentials: true }).pipe(
      catchError(() => [null]),
      finalize(() => this.clearSession()),
    );
  }

  clearSession(): void {
    this.accessToken.set(null);
    this.user.set(null);
    this.permissions.set([]);
  }

  hasAll(...permissions: string[]): boolean {
    const granted = this.permissions();
    return permissions.every((permission) => granted.includes(permission));
  }

  hasAll$(...permissions: string[]): Observable<boolean> {
    return this.permissions$.pipe(
      map((granted) => permissions.every((permission) => granted.includes(permission))),
    );
  }

  private applyToken(response: AuthResponse): void {
    this.accessToken.set(response.accessToken);
    this.user.set(response.user);
  }
}
