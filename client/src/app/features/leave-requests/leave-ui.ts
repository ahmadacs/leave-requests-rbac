import { LeaveStatus } from '../../core/models';

export function statusClasses(status: LeaveStatus): string {
  switch (status) {
    case 'Approved':
      return 'bg-emerald-100 text-emerald-700';
    case 'Rejected':
      return 'bg-red-100 text-red-700';
    case 'Cancelled':
      return 'bg-slate-200 text-slate-600';
    default:
      return 'bg-amber-100 text-amber-700';
  }
}

export function formatDate(value: string): string {
  const [year, month, day] = value.split('-').map(Number);
  if (!year || !month || !day) {
    return value;
  }
  return new Date(Date.UTC(year, month - 1, day)).toLocaleDateString(undefined, {
    year: 'numeric',
    month: 'short',
    day: 'numeric',
    timeZone: 'UTC',
  });
}
