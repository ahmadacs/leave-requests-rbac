import { Component, inject, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { AuthService } from '../../core/auth/auth.service';
import { safeReturnUrl } from '../../core/auth/return-url';
import { messageOf } from '../../core/http/api-error';

interface DemoAccount {
  readonly email: string;
  readonly role: string;
}

interface DemoTenant {
  readonly tenant: string;
  readonly accounts: readonly DemoAccount[];
}

@Component({
  selector: 'app-login',
  imports: [ReactiveFormsModule],
  templateUrl: './login.html',
})
export class Login {
  private readonly formBuilder = inject(NonNullableFormBuilder);
  private readonly auth = inject(AuthService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  protected readonly form = this.formBuilder.group({
    email: this.formBuilder.control('', [Validators.required, Validators.email]),
    password: this.formBuilder.control('', [Validators.required]),
  });

  protected readonly demoPassword = 'Passw0rd!';
  protected readonly demoTenants: readonly DemoTenant[] = [
    {
      tenant: 'Northwind',
      accounts: [
        { email: 'admin@northwind.test', role: 'Administrator' },
        { email: 'hr@northwind.test', role: 'HR' },
        { email: 'sales.manager@northwind.test', role: 'Manager' },
        { email: 'sales.employee1@northwind.test', role: 'Employee' },
        { email: 'sales.employee2@northwind.test', role: 'Employee' },
        { email: 'finance.manager@northwind.test', role: 'Manager' },
        { email: 'finance.employee1@northwind.test', role: 'Employee' },
        { email: 'finance.employee2@northwind.test', role: 'Employee' },
      ],
    },
    {
      tenant: 'Contoso',
      accounts: [
        { email: 'admin@contoso.test', role: 'Administrator' },
        { email: 'manager@contoso.test', role: 'Manager' },
        { email: 'employee@contoso.test', role: 'Employee' },
      ],
    },
  ];

  protected readonly error = signal<string | null>(null);
  protected readonly submitting = signal(false);
  protected readonly selectedEmail = signal<string | null>(null);

  protected useDemoAccount(email: string): void {
    this.form.setValue({ email, password: this.demoPassword });
    this.error.set(null);
    this.selectedEmail.set(email);
  }

  protected submit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.submitting.set(true);
    this.error.set(null);

    const { email, password } = this.form.getRawValue();
    this.auth.login(email, password).subscribe({
      next: () => {
        const returnUrl = safeReturnUrl(this.route.snapshot.queryParamMap.get('returnUrl'));
        void this.router.navigateByUrl(returnUrl);
      },
      error: (error: unknown) => {
        this.error.set(messageOf(error));
        this.submitting.set(false);
      },
    });
  }
}
