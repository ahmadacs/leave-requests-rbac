import { Component, inject, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { AuthService } from '../../core/auth/auth.service';
import { messageOf } from '../../core/http/api-error';
import { LeaveRequest, LeaveScope, LeaveType } from '../../core/models';
import { HasPermissionDirective } from '../../core/permissions/has-permission.directive';
import { LeaveRequestsListBase } from './leave-requests-list.base';

@Component({
  selector: 'app-my-requests',
  imports: [ReactiveFormsModule, HasPermissionDirective],
  templateUrl: './my-requests.html',
})
export class MyRequests extends LeaveRequestsListBase {
  private readonly formBuilder = inject(NonNullableFormBuilder);

  protected readonly auth = inject(AuthService);

  protected override get scope(): LeaveScope {
    return 'own';
  }

  protected readonly showForm = signal(false);
  protected readonly types: LeaveType[] = ['Annual', 'Sick', 'Unpaid'];

  protected readonly form = this.formBuilder.group({
    type: this.formBuilder.control<LeaveType>('Annual', Validators.required),
    startDate: this.formBuilder.control('', Validators.required),
    endDate: this.formBuilder.control('', Validators.required),
    reason: this.formBuilder.control('', [Validators.required, Validators.maxLength(1000)]),
  });

  protected toggleForm(): void {
    this.showForm.update((open) => !open);
    this.error.set(null);
  }

  protected submit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.api.create(this.form.getRawValue()).subscribe({
      next: () => {
        this.form.reset({ type: 'Annual', startDate: '', endDate: '', reason: '' });
        this.showForm.set(false);
        this.reload();
      },
      error: (error: unknown) => this.error.set(messageOf(error)),
    });
  }

  protected cancel(request: LeaveRequest): void {
    this.api.cancel(request.id).subscribe({
      next: () => this.reload(),
      error: (error: unknown) => this.error.set(messageOf(error)),
    });
  }
}
