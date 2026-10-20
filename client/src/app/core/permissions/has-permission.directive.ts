import { Directive, TemplateRef, ViewContainerRef, effect, inject, input } from '@angular/core';
import { AuthService } from '../auth/auth.service';

/**
 * Structural directive: renders its content only when the signed-in user holds
 * every listed permission.
 *
 * ```html
 * <button *appHasPermission="'leaves.approve'">Approve</button>
 * <button *appHasPermission="['roles.view', 'roles.manage']">Edit</button>
 * ```
 *
 * It reacts to permission changes (the effect re-runs when the permissions
 * signal updates, e.g. after a role change).
 */
@Directive({
  selector: '[appHasPermission]',
  standalone: true,
})
export class HasPermissionDirective {
  private readonly auth = inject(AuthService);
  private readonly templateRef = inject(TemplateRef<unknown>);
  private readonly viewContainer = inject(ViewContainerRef);

  private hasView = false;

  readonly appHasPermission = input.required<string | readonly string[]>();

  constructor() {
    effect(() => {
      // Depend on permissions and on the input so the view updates for both.
      const required = this.normalize(this.appHasPermission());
      const granted = this.auth.permissions();
      this.render(required.every((permission) => granted.includes(permission)));
    });
  }

  private normalize(value: string | readonly string[]): readonly string[] {
    return typeof value === 'string' ? [value] : value;
  }

  private render(allowed: boolean): void {
    if (allowed && !this.hasView) {
      this.viewContainer.createEmbeddedView(this.templateRef);
      this.hasView = true;
    } else if (!allowed && this.hasView) {
      this.viewContainer.clear();
      this.hasView = false;
    }
  }
}
