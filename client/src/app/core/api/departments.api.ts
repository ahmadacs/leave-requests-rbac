import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { Department } from '../models';

@Injectable({ providedIn: 'root' })
export class DepartmentsApi {
  private readonly http = inject(HttpClient);

  list(): Observable<Department[]> {
    return this.http.get<Department[]>('/api/departments');
  }
}
