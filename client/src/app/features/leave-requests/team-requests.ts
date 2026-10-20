import { Component, computed, inject } from '@angular/core';
import { AuthService } from '../../core/auth/auth.service';
import { messageOf } from '../../core/http/api-error';
import { LeaveRequest, LeaveScope, PERMISSIONS } from '../../core/models';
import { HasPermissionDirective } from '../../core/permissions/has-permission.directive';
import { LeaveRequestsListBase } from './leave-requests-list.base';

@Component({
  selector: 'app-team-requests',
  imports: [HasPermissionDirective],
  templateUrl: './team-requests.html',
})
export class TeamRequests extends LeaveRequestsListBase {
  protected readonly auth = inject(AuthService);
  protected readonly canApproveLong = computed(() =>
    this.auth.hasAll(PERMISSIONS.leaves.approveLong),
  );

  protected override get scope(): LeaveScope {
    return 'team';
  }

  protected isOwn(request: LeaveRequest): boolean {
    return request.employeeId === this.auth.user()?.id;
  }

  protected needsHr(request: LeaveRequest): boolean {
    const start = Date.parse(`${request.startDate}T00:00:00Z`);
    const end = Date.parse(`${request.endDate}T00:00:00Z`);
    const days = Math.floor((end - start) / 86_400_000) + 1;
    return days > 10;
  }

  protected canDecide(request: LeaveRequest): boolean {
    return !this.isOwn(request) && (!this.needsHr(request) || this.canApproveLong());
  }

  protected approve(request: LeaveRequest): void {
    this.api.approve(request.id).subscribe({
      next: () => this.reload(),
      error: (error: unknown) => this.error.set(messageOf(error)),
    });
  }

  protected reject(request: LeaveRequest): void {
    this.api.reject(request.id).subscribe({
      next: () => this.reload(),
      error: (error: unknown) => this.error.set(messageOf(error)),
    });
  }
}
