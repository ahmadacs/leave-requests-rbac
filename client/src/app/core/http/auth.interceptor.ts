import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, switchMap, throwError } from 'rxjs';
import { AuthService } from '../auth/auth.service';

const RETRY_HEADER = 'x-auth-retry';

/**
 * Adds the Bearer token to every same-origin API call except the auth
 * endpoints. On a 401 it refreshes once (shared across parallel requests) and
 * retries; if the refresh fails it clears the session and sends the user to the
 * login page with a returnUrl.
 */
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const auth = inject(AuthService);
  const router = inject(Router);

  const isAuthEndpoint = req.url.startsWith('/api/auth/');
  const token = auth.currentToken();

  const authorized =
    token && !isAuthEndpoint
      ? req.clone({ setHeaders: { Authorization: `Bearer ${token}` } })
      : req;

  return next(authorized).pipe(
    catchError((error: unknown) => {
      const status = error instanceof HttpErrorResponse ? error.status : undefined;
      const alreadyRetried = req.headers.has(RETRY_HEADER);

      if (status === 401 && !isAuthEndpoint && !alreadyRetried) {
        return auth.refresh().pipe(
          switchMap(() =>
            next(
              req.clone({
                setHeaders: {
                  Authorization: `Bearer ${auth.currentToken() ?? ''}`,
                  [RETRY_HEADER]: '1',
                },
              }),
            ),
          ),
          catchError((refreshError: unknown) => {
            auth.clearSession();
            void router.navigate(['/login'], {
              queryParams: { returnUrl: router.url },
            });
            return throwError(() => refreshError);
          }),
        );
      }

      return throwError(() => error);
    }),
  );
};
