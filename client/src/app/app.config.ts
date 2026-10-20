import { provideHttpClient, withInterceptors } from '@angular/common/http';
import {
  ApplicationConfig,
  inject,
  provideAppInitializer,
  provideBrowserGlobalErrorListeners,
  provideZoneChangeDetection,
} from '@angular/core';
import { provideRouter } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { routes } from './app.routes';
import { AuthService } from './core/auth/auth.service';
import { problemDetailsInterceptor } from './core/http/api-error';
import { authInterceptor } from './core/http/auth.interceptor';
import { NotificationsService } from './core/realtime/notifications.service';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideZoneChangeDetection({ eventCoalescing: true }),
    provideRouter(routes),
    // problemDetails runs outermost so the auth interceptor still sees the raw
    // 401 from HttpErrorResponse, and components get a typed ApiError.
    provideHttpClient(withInterceptors([problemDetailsInterceptor, authInterceptor])),
    // Restore the session from the refresh cookie before the first route runs.
    provideAppInitializer(() => firstValueFrom(inject(AuthService).restoreSession())),
    // Keep the live permission connection alive for the app lifetime.
    provideAppInitializer(() => {
      inject(NotificationsService);
    }),
  ],
};
