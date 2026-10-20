import { Routes } from '@angular/router';
import { authGuard } from './core/guards/auth.guard';
import { guestGuard } from './core/guards/guest.guard';
import { permissionGuard } from './core/guards/permission.guard';
import { PERMISSIONS } from './core/models';

export const routes: Routes = [
  {
    path: 'login',
    canActivate: [guestGuard],
    loadComponent: () => import('./features/login/login').then((m) => m.Login),
  },
  {
    path: 'forbidden',
    canActivate: [authGuard],
    loadComponent: () => import('./features/forbidden/forbidden').then((m) => m.Forbidden),
  },
  {
    path: '',
    canActivate: [authGuard],
    loadComponent: () => import('./features/shell/shell').then((m) => m.Shell),
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'my-requests' },
      {
        path: 'my-requests',
        canActivate: [permissionGuard],
        data: { permissions: [PERMISSIONS.leaves.viewOwn] },
        loadComponent: () =>
          import('./features/leave-requests/my-requests').then((m) => m.MyRequests),
      },
      {
        path: 'team-requests',
        canActivate: [permissionGuard],
        data: { permissions: [PERMISSIONS.leaves.viewTeam] },
        loadComponent: () =>
          import('./features/leave-requests/team-requests').then((m) => m.TeamRequests),
      },
      {
        path: 'all-requests',
        canActivate: [permissionGuard],
        data: { permissions: [PERMISSIONS.leaves.viewAll] },
        loadComponent: () =>
          import('./features/leave-requests/all-requests').then((m) => m.AllRequests),
      },
      {
        path: 'roles',
        canActivate: [permissionGuard],
        data: { permissions: [PERMISSIONS.roles.view] },
        loadComponent: () => import('./features/roles/roles').then((m) => m.Roles),
      },
      {
        path: 'users',
        canActivate: [permissionGuard],
        data: { permissions: [PERMISSIONS.users.view] },
        loadComponent: () => import('./features/users/users').then((m) => m.Users),
      },
      {
        path: 'audit-logs',
        canActivate: [permissionGuard],
        data: { permissions: [PERMISSIONS.audit.view] },
        loadComponent: () =>
          import('./features/audit-logs/audit-logs').then((m) => m.AuditLogs),
      },
    ],
  },
  { path: '**', redirectTo: '' },
];
