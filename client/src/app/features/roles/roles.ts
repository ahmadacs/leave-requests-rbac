import { Component, computed, inject, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RolesApi } from '../../core/api/roles.api';
import { AuthService } from '../../core/auth/auth.service';
import { messageOf } from '../../core/http/api-error';
import { PERMISSIONS, PermissionInfo, Role } from '../../core/models';

interface PermissionGroup {
  name: string;
  permissions: PermissionInfo[];
}

@Component({
  selector: 'app-roles',
  imports: [ReactiveFormsModule],
  templateUrl: './roles.html',
})
export class Roles {
  private readonly api = inject(RolesApi);
  private readonly formBuilder = inject(NonNullableFormBuilder);

  protected readonly auth = inject(AuthService);
  protected readonly roles = signal<Role[]>([]);
  protected readonly catalogue = signal<PermissionInfo[]>([]);
  protected readonly error = signal<string | null>(null);
  protected readonly notice = signal<string | null>(null);
  protected readonly selected = signal<Role | null>(null);
  protected readonly creating = signal(false);
  protected readonly draftPermissions = signal<readonly string[]>([]);
  protected readonly saving = signal(false);

  protected readonly canManage = computed(() => this.auth.hasAll(PERMISSIONS.roles.manage));
  protected readonly isEditable = computed(
    () =>
      this.canManage() &&
      (this.creating() || (this.selected() !== null && !this.selected()!.isSystemRole)),
  );
  protected readonly groups = computed<PermissionGroup[]>(() => {
    const byGroup = new Map<string, PermissionInfo[]>();
    for (const permission of this.catalogue()) {
      const bucket = byGroup.get(permission.group) ?? [];
      bucket.push(permission);
      byGroup.set(permission.group, bucket);
    }
    return [...byGroup.entries()]
      .map(([name, permissions]) => ({ name, permissions }))
      .sort((a, b) => a.name.localeCompare(b.name));
  });

  protected readonly form = this.formBuilder.group({
    name: this.formBuilder.control('', [Validators.required, Validators.maxLength(100)]),
    description: this.formBuilder.control('', [Validators.maxLength(500)]),
  });

  constructor() {
    this.api.list().subscribe({
      next: (roles) => this.roles.set(roles),
      error: (error: unknown) => this.error.set(messageOf(error)),
    });
    this.api.permissions().subscribe({
      next: (permissions) => this.catalogue.set(permissions),
      error: (error: unknown) => this.error.set(messageOf(error)),
    });
  }

  protected hasDraft(code: string): boolean {
    return this.draftPermissions().includes(code);
  }

  protected select(role: Role): void {
    this.creating.set(false);
    this.selected.set(role);
    this.error.set(null);
    this.notice.set(null);
    this.form.reset({ name: role.name, description: role.description ?? '' });
    this.draftPermissions.set([...role.permissions]);
  }

  protected startCreate(): void {
    this.creating.set(true);
    this.selected.set(null);
    this.error.set(null);
    this.notice.set(null);
    this.form.reset({ name: '', description: '' });
    this.draftPermissions.set([]);
  }

  protected togglePermission(code: string): void {
    if (!this.isEditable()) {
      return;
    }
    const current = this.draftPermissions();
    this.draftPermissions.set(
      current.includes(code) ? current.filter((c) => c !== code) : [...current, code],
    );
  }

  protected toggleGroup(group: PermissionGroup, checked: boolean): void {
    if (!this.isEditable()) {
      return;
    }
    const codes = group.permissions.map((permission) => permission.code);
    const current = new Set(this.draftPermissions());
    for (const code of codes) {
      if (checked) {
        current.add(code);
      } else {
        current.delete(code);
      }
    }
    this.draftPermissions.set([...current]);
  }

  protected groupFullySelected(group: PermissionGroup): boolean {
    return group.permissions.every((permission) => this.hasDraft(permission.code));
  }

  protected save(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.saving.set(true);
    this.error.set(null);
    this.notice.set(null);

    const value = this.form.getRawValue();
    const body = {
      name: value.name,
      description: value.description.length > 0 ? value.description : null,
      permissions: [...this.draftPermissions()],
    };

    const selected = this.selected();
    const request$ =
      this.creating() || selected === null
        ? this.api.create(body)
        : this.api.update(selected.id, body);

    request$.subscribe({
      next: (role) => {
        this.saving.set(false);
        this.notice.set(`Saved '${role.name}'.`);
        this.refresh(role.id);
      },
      error: (error: unknown) => {
        this.saving.set(false);
        this.error.set(messageOf(error));
      },
    });
  }

  protected remove(role: Role): void {
    this.error.set(null);
    this.notice.set(null);
    this.api.remove(role.id).subscribe({
      next: () => {
        this.notice.set(`Deleted '${role.name}'.`);
        this.selected.set(null);
        this.creating.set(false);
        this.refresh(null);
      },
      error: (error: unknown) => this.error.set(messageOf(error)),
    });
  }

  private refresh(reselectId: string | null): void {
    this.api.list().subscribe({
      next: (roles) => {
        this.roles.set(roles);
        const match = reselectId ? (roles.find((role) => role.id === reselectId) ?? null) : null;
        if (match) {
          this.select(match);
        }
      },
      error: (error: unknown) => this.error.set(messageOf(error)),
    });
  }
}
