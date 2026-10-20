import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { PermissionInfo, Role } from '../models';

export interface SaveRole {
  name: string;
  description: string | null;
  permissions: string[];
}

@Injectable({ providedIn: 'root' })
export class RolesApi {
  private readonly http = inject(HttpClient);

  list(): Observable<Role[]> {
    return this.http.get<Role[]>('/api/roles');
  }

  create(body: SaveRole): Observable<Role> {
    return this.http.post<Role>('/api/roles', body);
  }

  update(id: string, body: SaveRole): Observable<Role> {
    return this.http.put<Role>(`/api/roles/${id}`, body);
  }

  remove(id: string): Observable<void> {
    return this.http.delete<void>(`/api/roles/${id}`);
  }

  permissions(): Observable<PermissionInfo[]> {
    return this.http.get<PermissionInfo[]>('/api/permissions');
  }
}
