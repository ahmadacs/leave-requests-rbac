import { DatePipe, JsonPipe } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { AuditLogsApi } from '../../core/api/audit-logs.api';
import { messageOf } from '../../core/http/api-error';
import { AuditLog } from '../../core/models';

@Component({
  selector: 'app-audit-logs',
  imports: [DatePipe, JsonPipe],
  templateUrl: './audit-logs.html',
})
export class AuditLogs {
  private readonly api = inject(AuditLogsApi);

  protected readonly logs = signal<AuditLog[]>([]);
  protected readonly error = signal<string | null>(null);
  protected readonly loading = signal(false);

  constructor() {
    this.load();
  }

  protected load(): void {
    this.loading.set(true);
    this.error.set(null);
    this.api.list().subscribe({
      next: (logs) => {
        this.logs.set(logs);
        this.loading.set(false);
      },
      error: (error: unknown) => {
        this.error.set(messageOf(error));
        this.loading.set(false);
      },
    });
  }
}
