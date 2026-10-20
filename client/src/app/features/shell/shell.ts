import { Component, computed, inject } from '@angular/core';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AuthService } from '../../core/auth/auth.service';
import { PERMISSIONS } from '../../core/models';
import { HasPermissionDirective } from '../../core/permissions/has-permission.directive';

interface NavItem {
  path: string;
  label: string;
  permission: string;
}

@Component({
  selector: 'app-shell',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, HasPermissionDirective],
  templateUrl: './shell.html',
})
export class Shell {
  protected readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  protected readonly navItems: NavItem[] = [
    { path: '/my-requests', label: 'My requests', permission: PERMISSIONS.leaves.viewOwn },
    { path: '/team-requests', label: 'Team', permission: PERMISSIONS.leaves.viewTeam },
    { path: '/all-requests', label: 'All requests', permission: PERMISSIONS.leaves.viewAll },
    { path: '/roles', label: 'Roles', permission: PERMISSIONS.roles.view },
    { path: '/users', label: 'Users', permission: PERMISSIONS.users.view },
    { path: '/audit-logs', label: 'Audit log', permission: PERMISSIONS.audit.view },
  ];

  protected readonly initials = computed(() => {
    const name = this.auth.user()?.name ?? '';
    return name
      .split(' ')
      .filter(Boolean)
      .slice(0, 2)
      .map((part) => part[0]?.toUpperCase() ?? '')
      .join('');
  });

  protected signOut(): void {
    this.auth.logout().subscribe(() => void this.router.navigate(['/login']));
  }
}
