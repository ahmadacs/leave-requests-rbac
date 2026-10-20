import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from '../auth/auth.service';
import { safeReturnUrl } from '../auth/return-url';

/** Keeps signed-in users away from the login page. */
export const guestGuard: CanActivateFn = (route) => {
  const auth = inject(AuthService);
  const router = inject(Router);

  if (!auth.isAuthenticated()) {
    return true;
  }

  const returnUrl = safeReturnUrl(route.queryParamMap.get('returnUrl'));
  return router.createUrlTree([returnUrl]);
};
