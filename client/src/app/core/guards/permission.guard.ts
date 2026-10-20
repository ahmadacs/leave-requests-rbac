import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { catchError, map, of } from 'rxjs';
import { AuthService } from '../auth/auth.service';

/**
 * Re-reads /api/me before each guarded navigation, then checks the route's
 * `data.permissions` (all are required). This is what makes a role change take
 * effect without re-login: the server cache is invalidated on the change and
 * the next navigation picks up the new permission set.
 */
export const permissionGuard: CanActivateFn = (route) => {
  const auth = inject(AuthService);
  const router = inject(Router);

  const required = (route.data['permissions'] as string[] | undefined) ?? [];

  return auth.reloadProfile().pipe(
    map(() =>
      required.length === 0 || auth.hasAll(...required)
        ? true
        : router.createUrlTree(['/forbidden']),
    ),
    // A failed profile read (e.g. refresh failed) is handled by the auth
    // interceptor, which clears the session and redirects to /login.
    catchError(() => of(false)),
  );
};
