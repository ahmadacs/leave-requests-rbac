import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { CreateUser, User } from '../models';

@Injectable({ providedIn: 'root' })
export class UsersApi {
  private readonly http = inject(HttpClient);

  list(): Observable<User[]> {
    return this.http.get<User[]>('/api/users');
  }

  create(body: CreateUser): Observable<User> {
    return this.http.post<User>('/api/users', body);
  }

  setRoles(id: string, roles: string[]): Observable<User> {
    return this.http.put<User>(`/api/users/${id}/roles`, { roles });
  }

  activate(id: string): Observable<User> {
    return this.http.post<User>(`/api/users/${id}/activate`, {});
  }

  deactivate(id: string): Observable<User> {
    return this.http.post<User>(`/api/users/${id}/deactivate`, {});
  }
}
