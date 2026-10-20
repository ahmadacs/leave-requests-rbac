import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { CreateLeaveRequest, LeaveRequest, LeaveScope, LeaveStatus } from '../models';

@Injectable({ providedIn: 'root' })
export class LeaveRequestsApi {
  private readonly http = inject(HttpClient);

  list(scope: LeaveScope, status?: LeaveStatus | null): Observable<LeaveRequest[]> {
    let params = new HttpParams().set('scope', scope);
    if (status) {
      params = params.set('status', status);
    }
    return this.http.get<LeaveRequest[]>('/api/leave-requests', { params });
  }

  get(id: string): Observable<LeaveRequest> {
    return this.http.get<LeaveRequest>(`/api/leave-requests/${id}`);
  }

  create(body: CreateLeaveRequest): Observable<LeaveRequest> {
    return this.http.post<LeaveRequest>('/api/leave-requests', body);
  }

  cancel(id: string): Observable<LeaveRequest> {
    return this.http.post<LeaveRequest>(`/api/leave-requests/${id}/cancel`, {});
  }

  approve(id: string): Observable<LeaveRequest> {
    return this.http.post<LeaveRequest>(`/api/leave-requests/${id}/approve`, {});
  }

  reject(id: string): Observable<LeaveRequest> {
    return this.http.post<LeaveRequest>(`/api/leave-requests/${id}/reject`, {});
  }

  exportCsv(): Observable<Blob> {
    return this.http.get('/api/leave-requests/export', { responseType: 'blob' });
  }
}
