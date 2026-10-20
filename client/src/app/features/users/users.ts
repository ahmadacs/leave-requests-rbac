import { Component, computed, inject, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { DepartmentsApi } from '../../core/api/departments.api';
import { RolesApi } from '../../core/api/roles.api';
import { UsersApi } from '../../core/api/users.api';
import { AuthService } from '../../core/auth/auth.service';
import { messageOf } from '../../core/http/api-error';
import { Department, PERMISSIONS, Role, User } from '../../core/models';

@Component({
  selector: 'app-users',
  imports: [ReactiveFormsModule],
  templateUrl: './users.html',
})
export class Users {
  private readonly api = inject(UsersApi);
  private readonly rolesApi = inject(RolesApi);
  private readonly departmentsApi = inject(DepartmentsApi);
  private readonly formBuilder = inject(NonNullableFormBuilder);

  protected readonly auth = inject(AuthService);
  protected readonly users = signal<User[]>([]);
  protected readonly roles = signal<Role[]>([]);
  protected readonly departments = signal<Department[]>([]);
  protected readonly error = signal<string | null>(null);
  protected readonly notice = signal<string | null>(null);
  protected readonly showCreate = signal(false);
  protected readonly busy = signal(false);
  protected readonly createRoles = signal<readonly string[]>([]);
  protected readonly editingId = signal<string | null>(null);
  protected readonly draftRoles = signal<readonly string[]>([]);

  protected readonly canManage = computed(() => this.auth.hasAll(PERMISSIONS.users.manage));

  protected readonly form = this.formBuilder.group({
    email: this.formBuilder.control('', [Validators.required, Validators.email]),
    fullName: this.formBuilder.control('', [Validators.required, Validators.maxLength(200)]),
    departmentId: this.formBuilder.control(''),
    password: this.formBuilder.control(''),
  });

  constructor() {
    this.loadUsers();
    this.rolesApi.list().subscribe({
      next: (roles) => this.roles.set(roles),
      error: (error: unknown) => this.error.set(messageOf(error)),
    });
    this.departmentsApi.list().subscribe({
      next: (departments) => this.departments.set(departments),
      error: (error: unknown) => this.error.set(messageOf(error)),
    });
  }

  protected departmentName(user: User): string {
    if (!user.departmentId) {
      return '—';
    }
    return this.departments().find((d) => d.id === user.departmentId)?.name ?? '—';
  }

  protected isSelf(user: User): boolean {
    return user.id === this.auth.user()?.id;
  }

  protected toggleCreateRole(name: string): void {
    this.createRoles.update((current) =>
      current.includes(name) ? current.filter((n) => n !== name) : [...current, name],
    );
  }

  protected toggleDraftRole(name: string): void {
    this.draftRoles.update((current) =>
      current.includes(name) ? current.filter((n) => n !== name) : [...current, name],
    );
  }

  protected openCreate(): void {
    this.showCreate.update((open) => !open);
    this.error.set(null);
    this.notice.set(null);
    this.form.reset({ email: '', fullName: '', departmentId: '', password: '' });
    this.createRoles.set([]);
  }

  protected create(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.busy.set(true);
    this.error.set(null);
    const { email, fullName, departmentId, password } = this.form.getRawValue();

    this.api
      .create({
        email,
        fullName,
        departmentId: departmentId.length > 0 ? departmentId : null,
        password: password.length > 0 ? password : null,
        roles: [...this.createRoles()],
      })
      .subscribe({
        next: (user) => {
          this.busy.set(false);
          this.notice.set(`Created '${user.email}'.`);
          this.showCreate.set(false);
          this.loadUsers();
        },
        error: (error: unknown) => {
          this.busy.set(false);
          this.error.set(messageOf(error));
        },
      });
  }

  protected editRoles(user: User): void {
    this.editingId.set(user.id);
    this.draftRoles.set([...user.roles]);
    this.error.set(null);
    this.notice.set(null);
  }

  protected cancelEdit(): void {
    this.editingId.set(null);
    this.draftRoles.set([]);
  }

  protected saveRoles(user: User): void {
    if (this.isSelf(user)) {
      this.error.set('You cannot change your own roles.');
      return;
    }

    this.busy.set(true);
    this.error.set(null);
    this.api.setRoles(user.id, [...this.draftRoles()]).subscribe({
      next: (updated) => {
        this.busy.set(false);
        this.notice.set(`Updated roles for '${updated.email}'.`);
        this.cancelEdit();
        this.loadUsers();
      },
      error: (error: unknown) => {
        this.busy.set(false);
        this.error.set(messageOf(error));
      },
    });
  }

  protected toggleActive(user: User): void {
    this.error.set(null);
    this.notice.set(null);
    const action$ = user.isActive ? this.api.deactivate(user.id) : this.api.activate(user.id);
    action$.subscribe({
      next: () => this.loadUsers(),
      error: (error: unknown) => this.error.set(messageOf(error)),
    });
  }

  private loadUsers(): void {
    this.api.list().subscribe({
      next: (users) => this.users.set(users),
      error: (error: unknown) => this.error.set(messageOf(error)),
    });
  }
}
