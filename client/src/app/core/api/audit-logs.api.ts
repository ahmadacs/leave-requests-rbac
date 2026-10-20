import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { AuditLog } from '../models';

@Injectable({ providedIn: 'root' })
export class AuditLogsApi {
  private readonly http = inject(HttpClient);

  list(take = 200): Observable<AuditLog[]> {
    return this.http.get<AuditLog[]>('/api/audit-logs', { params: { take } });
  }
}
