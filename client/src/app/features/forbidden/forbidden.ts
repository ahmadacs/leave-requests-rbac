import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';

@Component({
  selector: 'app-forbidden',
  imports: [RouterLink],
  template: `
    <div
      class="flex min-h-screen flex-col items-center justify-center gap-4 bg-slate-100 px-4 text-center"
    >
      <span class="text-5xl font-bold text-slate-300">403</span>
      <h1 class="text-xl font-semibold text-slate-800">You don't have access to this page</h1>
      <p class="max-w-md text-sm text-slate-500">
        Your account is missing the permission this page requires. Contact an administrator if you
        think this is a mistake.
      </p>
      <a
        routerLink="/"
        class="rounded-md bg-indigo-600 px-4 py-2 text-sm font-semibold text-white hover:bg-indigo-700"
      >
        Back to my requests
      </a>
    </div>
  `,
})
export class Forbidden {}
