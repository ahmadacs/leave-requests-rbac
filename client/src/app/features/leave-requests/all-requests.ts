import { Component, computed, inject, signal } from '@angular/core';
import { AuthService } from '../../core/auth/auth.service';
import { messageOf } from '../../core/http/api-error';
import { LeaveRequest, LeaveScope, LeaveStatus, LeaveType, PERMISSIONS } from '../../core/models';
import { HasPermissionDirective } from '../../core/permissions/has-permission.directive';
import { LeaveRequestsListBase } from './leave-requests-list.base';

@Component({
  selector: 'app-all-requests',
  imports: [HasPermissionDirective],
  templateUrl: './all-requests.html',
})
export class AllRequests extends LeaveRequestsListBase {
  private readonly auth = inject(AuthService);

  protected override get scope(): LeaveScope {
    return 'all';
  }

  protected readonly typeFilter = signal<LeaveType | ''>('');
  protected readonly typeOptions: LeaveType[] = ['Annual', 'Sick', 'Unpaid'];
  protected readonly canExport = computed(() =>
    this.auth.hasAll(PERMISSIONS.leaves.viewAll, PERMISSIONS.leaves.export),
  );
  protected readonly canApproveLong = computed(() =>
    this.auth.hasAll(PERMISSIONS.leaves.approveLong),
  );
  protected readonly managerApprovalLimitDays = 10;

  private readonly order: Record<LeaveStatus, number> = {
    Pending: 0,
    Approved: 1,
    Rejected: 2,
    Cancelled: 3,
  };

  protected readonly visible = computed(() => {
    const type = this.typeFilter();
    return this.requests().filter((request) => !type || request.type === type);
  });

  protected override sort(items: LeaveRequest[]): LeaveRequest[] {
    return [...items].sort(
      (a, b) =>
        this.order[a.status] - this.order[b.status] || a.startDate.localeCompare(b.startDate),
    );
  }

  protected onTypeChange(value: string): void {
    this.typeFilter.set(value as LeaveType | '');
  }

  protected isOwn(request: LeaveRequest): boolean {
    return request.employeeId === this.auth.user()?.id;
  }

  protected durationDays(request: LeaveRequest): number {
    const start = Date.parse(`${request.startDate}T00:00:00Z`);
    const end = Date.parse(`${request.endDate}T00:00:00Z`);
    return Math.floor((end - start) / 86_400_000) + 1;
  }

  protected needsHr(request: LeaveRequest): boolean {
    return this.durationDays(request) > this.managerApprovalLimitDays;
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

  protected exportCsv(): void {
    this.api.exportCsv().subscribe({
      next: (blob) => {
        const url = URL.createObjectURL(blob);
        const anchor = document.createElement('a');
        anchor.href = url;
        anchor.download = 'leave-requests.csv';
        anchor.click();
        URL.revokeObjectURL(url);
      },
      error: (error: unknown) => this.error.set(messageOf(error)),
    });
  }
}
