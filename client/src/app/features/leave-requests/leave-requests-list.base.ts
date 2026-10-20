import { Directive, computed, inject, linkedSignal, signal } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { LeaveRequestsApi } from '../../core/api/leave-requests.api';
import { messageOf } from '../../core/http/api-error';
import { LeaveRequest, LeaveScope, LeaveStatus } from '../../core/models';
import { formatDate, statusClasses } from './leave-ui';

@Directive()
export abstract class LeaveRequestsListBase {
  protected readonly api = inject(LeaveRequestsApi);

  protected readonly statusFilter = signal<LeaveStatus | ''>('');
  protected readonly filterOptions: LeaveStatus[] = [
    'Pending',
    'Approved',
    'Rejected',
    'Cancelled',
  ];

  protected readonly formatDate = formatDate;
  protected readonly statusClasses = statusClasses;

  protected readonly list = rxResource({
    params: () => ({ scope: this.scope, status: this.statusFilter() || null }),
    stream: ({ params }) => this.api.list(params.scope, params.status),
    defaultValue: [] as LeaveRequest[],
  });

  protected readonly requests = computed(() => this.sort(this.list.value()));
  protected readonly loading = computed(() => this.list.isLoading());
  protected readonly error = linkedSignal<string | null>(() => {
    const error = this.list.error();
    return error ? messageOf(error) : null;
  });

  protected abstract get scope(): LeaveScope;

  protected sort(items: LeaveRequest[]): LeaveRequest[] {
    return items;
  }

  protected reload(): void {
    this.list.reload();
  }

  protected onStatusChange(value: string): void {
    this.statusFilter.set(value as LeaveStatus | '');
  }
}
