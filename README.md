# Leave Requests with RBAC

A multi-tenant leave-request API (ASP.NET Core 10) with a role/permission system
where **roles are data, not code**, plus an Angular 22 client that renders itself
from the permissions the signed-in user actually holds.

- Design contract, permission catalogue and role matrix: [`DESIGN.md`](DESIGN.md).
- Server: `server/` (ASP.NET Core 10 API). Client: `client/` (Angular 22).

```
leave-requests-rbac/
├── DESIGN.md
├── README.md
├── server/
│   ├── LeaveRequests.sln          (open this in Visual Studio)
│   ├── LeaveRequests.slnx
│   ├── http/leave-requests.http
│   └── src/LeaveRequests.Api/
└── client/
```

---

## Prerequisites

- .NET SDK 10
- Node.js 22 + npm
- SQL Server 2022 (LocalDB, SQL Express or full SQL Server)
- The ASP.NET Core HTTPS dev certificate trusted once: `dotnet dev-certs https --trust`

---

## Building

The solution lives in `server/` — build from there (or open `server/LeaveRequests.sln`
in Visual Studio). There is intentionally no project at the repository root.

```powershell
cd server
dotnet build LeaveRequests.sln          # API
dotnet test tests/LeaveRequests.Api.Tests   # unit + integration tests (SQL Server)
cd ..\client; npx ng build              # client (production)
```

Integration tests use a unique SQL Server database per test and drop it
afterwards. The default server is `.\SQLEXPRESS`; override it with
`LEAVE_REQUESTS_TEST_SQLSERVER` (for example `(localdb)\MSSQLLocalDB`).

## Running the API

The JWT signing key is **not** committed; it lives in user secrets.

```powershell
cd server/src/LeaveRequests.Api
dotnet user-secrets set "Jwt:SigningKey" "dev-only-signing-key-change-me-at-least-32-chars"
dotnet run
```

- HTTPS: `https://localhost:7277` · HTTP: `http://localhost:5262`
- Storage: SQL Server database `LeaveRequests` (see `ConnectionStrings:Default`
  in `appsettings.json`). Schema is applied with EF Core migrations at startup
  (`Database.MigrateAsync`), then demo data is seeded idempotently.
- Add a migration after a model change:
  `dotnet ef migrations add <Name> --project src/LeaveRequests.Api --startup-project src/LeaveRequests.Api --output-dir Data/Migrations`.

## Running the client

The dev server proxies `/api` and the `/hubs` WebSocket to the API so the refresh cookie stays first-party
(see `client/proxy.conf.json` → `https://localhost:7277`).

```powershell
cd client
npm install
npm start
```

Open `http://localhost:4200`. The access token is kept **in memory** only; on a
page reload the session is restored from the refresh cookie by an app
initializer, so a hard refresh does not log you out.

---

## Demo accounts

One password for every seeded account: **`Passw0rd!`**

| Tenant | Email | Role | Department | Notable permissions |
|--------|-------|------|------------|---------------------|
| Northwind | `admin@northwind.test` | Administrator | Finance | everything |
| Northwind | `hr@northwind.test` | HR | Sales | `leaves.view.all`, `leaves.approve`, `leaves.approve.long`, `leaves.export` |
| Northwind | `sales.manager@northwind.test` | Manager | Sales | `leaves.view.team`, `leaves.approve` |
| Northwind | `sales.employee1@northwind.test` | Employee | Sales | `leaves.view.own`, `leaves.create`, `leaves.cancel` |
| Northwind | `sales.employee2@northwind.test` | Employee | Sales |  |
| Northwind | `finance.manager@northwind.test` | Manager | Finance |  |
| Northwind | `finance.employee1@northwind.test` | Employee | Finance |  |
| Northwind | `finance.employee2@northwind.test` | Employee | Finance |  |
| Contoso | `admin@contoso.test` | Administrator | General | everything (separate tenant) |
| Contoso | `manager@contoso.test` | Manager | General |  |
| Contoso | `employee@contoso.test` | Employee | General |  |

Sign in as `sales.manager@northwind.test` and `finance.employee1@northwind.test`
side by side: same role **name**, different tenant — and neither sees the other's
data. The Administrator holds `leaves.view.all`, so it can review any
department; a manager without that scope is limited to their own. The Sales
manager also owns a seeded pending request, which demonstrates the
no-self-approval rule (`403`). Managers can approve at most 10 calendar days;
the seeded 11-day request for `sales.employee2@northwind.test` requires HR
(`leaves.approve.long`) and is rejected with `403` for the manager.

---

## Permission catalogue

`resource.action`, grouped by module (single source of truth:
`Security/Permissions.cs`, mirrored in `client/src/app/core/models.ts`).

| Code | Group | Description |
|------|-------|-------------|
| `leaves.view.own` | Leaves | See your own leave requests |
| `leaves.create` | Leaves | Create a leave request for yourself |
| `leaves.cancel` | Leaves | Cancel your own pending request |
| `leaves.view.team` | Leaves | See the requests of your department |
| `leaves.approve` | Leaves | Approve or reject requests (department rule applies) |
| `leaves.approve.long` | Leaves | Approve requests longer than 10 calendar days |
| `leaves.view.all` | Leaves | See every request in the company |
| `leaves.export` | Leaves | Export requests to CSV |
| `users.view` | Users | See users |
| `users.manage` | Users | Create users, assign roles, activate/deactivate |
| `roles.view` | Roles | See roles |
| `roles.manage` | Roles | Create, edit and delete roles |
| `audit.view` | Audit | See the role and assignment audit log |

## Role matrix (seeded defaults, per tenant)

| Permission | Administrator | HR | Manager | Employee |
|------------|:---:|:---:|:---:|:---:|
| `leaves.view.own` | ✔ | ✔ | ✔ | ✔ |
| `leaves.create` | ✔ | ✔ | ✔ | ✔ |
| `leaves.cancel` | ✔ | ✔ | ✔ | ✔ |
| `leaves.view.team` | ✔ |  | ✔ |  |
| `leaves.approve` | ✔ | ✔ | ✔ |  |
| `leaves.approve.long` | ✔ | ✔ |  |  |
| `leaves.view.all` | ✔ | ✔ |  |  |
| `leaves.export` | ✔ | ✔ |  |  |
| `users.view` | ✔ |  |  |  |
| `users.manage` | ✔ |  |  |  |
| `roles.view` | ✔ |  |  |  |
| `roles.manage` | ✔ |  |  |  |
| `audit.view` | ✔ |  |  |  |

Only **Administrator** is a system role (always all permissions, not editable,
not deletable). HR / Manager / Employee are seeded defaults that remain editable;
the API only refuses to delete a role that is currently assigned to a user.

---

## API surface

| Method | Route | Requires |
|--------|-------|----------|
| POST | `/api/auth/login` | anonymous |
| POST | `/api/auth/refresh` | anonymous (refresh cookie) |
| POST | `/api/auth/logout` | anonymous (refresh cookie) |
| GET | `/api/me` | signed in |
| GET | `/api/leave-requests?scope=own\|team\|all&status=` | matching `leaves.view.*` |
| GET | `/api/leave-requests/{id}` | visible in one scope, else `404` |
| POST | `/api/leave-requests` | `leaves.create` |
| POST | `/api/leave-requests/{id}/cancel` | `leaves.cancel` + owner |
| POST | `/api/leave-requests/{id}/approve` | `leaves.approve` + not own + (same department unless `leaves.view.all`) + (≤10 days or `leaves.approve.long`) |
| POST | `/api/leave-requests/{id}/reject` | `leaves.approve` + not own + (same department unless `leaves.view.all`) + (≤10 days or `leaves.approve.long`) |
| GET | `/api/leave-requests/export` | `leaves.view.all` **and** `leaves.export` |
| GET | `/api/departments` | `users.view` |
| GET | `/api/users` | `users.view` |
| POST | `/api/users` | `users.manage` |
| PUT | `/api/users/{id}/roles` | `users.manage` (escalation guard) |
| POST | `/api/users/{id}/activate` `/deactivate` | `users.manage` |
| GET/POST | `/api/roles` | `roles.view` / `roles.manage` |
| PUT/DELETE | `/api/roles/{id}` | `roles.manage` |
| GET | `/api/permissions` | `roles.view` |
| GET | `/api/audit-logs?take=` | `audit.view` |
| WS | `/hubs/notifications` | signed in (access token in query) |

Errors are RFC 9457 problem details (`application/problem+json`):
`400` validation · `401` auth failure (identical message for every reason) ·
`403` missing permission or a denied rule · `404` not found **or out of scope** ·
`409` state conflict / overlap / duplicate.

Ready-made request collection with the `401`/`403`/`404`/`409` examples:
`server/http/leave-requests.http`.

---

## Written answers

### 1. Why do effective permissions live in the database and never in the JWT?

A JWT is self-contained, so anything baked into it is frozen until the token
expires. If permissions were claims, revoking a role would not take effect until
the user's token expired, and an administrator could not demote a compromised
account immediately. Instead the token carries only identity claims (`sub`,
`tenant_id`, `department_id`, `role`, `email`, `name`) and every request resolves
the caller's effective permission set **server-side, per request**, from
`RolePermission` via `IPermissionService`, cached per user in `IMemoryCache` and
invalidated whenever a role or assignment changes. The trade-off is one cached
lookup per request — cheap, and the source of truth is always the database.

### 2. How is tenant isolation enforced?

Two independent layers, so a mistake in one is not a data leak:

1. **A global EF Core query filter** on `LeaveRequest` (`AppDbContext`) appends
   `r.TenantId == currentTenantId` to every query, where the tenant comes from
   `ICurrentUser`. Cross-tenant rows are invisible even if a controller forgets
   to filter.
2. **Every write path re-derives the tenant** from `ICurrentUser` — a create
   cannot name another tenant, because the tenant and department on a new
   `LeaveRequest` are taken from the caller, not the payload.

IDs are GUIDs, so they are not guessable, and users/roles are also looked up
with an explicit `TenantId` check.

### 3. Why does an out-of-scope or cross-tenant read return 404 instead of 403?

`403` means "this resource exists but you may not see it"; `404` means "there is
nothing here for you". Returning `403` leaks existence — an employee could probe
ids and learn that a request exists in another department or another company.
`AppDbContext`/`LeaveRequestsController.FindVisibleAsync` first narrows the query
to the caller's visible scope and then looks for the id, so anything outside the
scope (including other tenants) simply does not resolve and yields `404`.
Deactivating a user or removing `leaves.view.own` removes that last own-scope
fallback too, so the row stops resolving as well. `403`
is reserved for actions the caller is authenticated to attempt but the *rule*
denies (e.g. self-approval, editing a system role).

### 4. Identity's default `RoleValidator` checks role names globally. What breaks, and how did you fix it?

ASP.NET Identity's `RoleValidator` calls `RoleManager.FindByNameAsync`, which
maps to an index that Identity makes **unique across the whole table** — so the
second tenant could never create a role named `Manager`, and editing a role name
could collide with another tenant's role. In a multi-tenant app role names must
be unique **per tenant**. The fix has two parts: the `RoleNameIndex` is made
non-unique and replaced with a composite unique index `(TenantId, NormalizedName)`
in `AppDbContext`, and the default validator is swapped for
`TenantRoleValidator`, registered with
`RemoveAll<IRoleValidator<ApplicationRole>>()` +
`AddScoped<IRoleValidator<ApplicationRole>, TenantRoleValidator>()`. It checks
for an existing role with the same name **within the caller's tenant only**.

### 5. How does the client reflect a permission change without re-login?

Roles come from the login response, but permissions are always read from
`GET /api/me` (`AuthService.reloadProfile`). After any role or assignment change
the API calls `IPermissionService.Invalidate(userId)`, and `permissionGuard`
re-fetches `/api/me` before the next guarded navigation, updating the
`permissions` signal before the route is allowed. Every
UI affordance is driven by that signal: the `permissionGuard` blocks routes
(`data.permissions`), and the `*appHasPermission` directive shows/hides controls
(and reacts live when the signal changes). The UI is a convenience only — the
server re-checks the same permissions on every request, so hiding a button is
never the security boundary.

---

## Bonus implementation notes

- **Live permission updates:** the API pushes `PermissionsChanged` over
  SignalR (`/hubs/notifications`) to every user affected by a role edit or an
  assignment/activation change. The Angular `NotificationsService` re-reads
  `/api/me`, so navigation and `*appHasPermission` controls update without a
  reload.
- **ABAC approval limit:** `LeaveRequest.DurationDays` is inclusive. A reviewer
  with `leaves.approve` may decide requests of up to 10 calendar days; longer
  requests additionally require `leaves.approve.long` (seeded for HR and
  Administrator). The UI disables the buttons and the domain returns `403`.
- **Audit log:** role create/update/delete, user create, role assignment and
  activation changes append an `AuditLog` row in the same unit of work. The
  read-only `/audit-logs` Angular page calls `GET /api/audit-logs` and requires
  `audit.view`.
- **Migrations + SQL Server tests:** `EnsureCreated` is gone; startup runs
  `Database.MigrateAsync()` with the checked-in SQL Server migration. The
  integration suite provisions a unique SQL Server database per test and drops
  it afterwards.
