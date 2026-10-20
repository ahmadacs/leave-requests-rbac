# Design — Leave Requests with RBAC

Multi-tenant leave-request system with role-based access control.
This document is the contract we build against: entities, the permission
catalogue, the role matrix and the API surface. Everything in `server/` and
`client/` must match it.

---

## 1. High-level architecture

```
leave-requests-rbac/
├── DESIGN.md                  <- this file
├── README.md                  <- how to run, demo accounts, answers
├── server/
│   ├── LeaveRequests.sln / .slnx      <- open either in Visual Studio
│   └── src/LeaveRequests.Api/         <- ASP.NET Core 10 Web API
└── client/                    <- Angular (standalone components)
```

Authorization is enforced at **three layers**:

| Layer | Mechanism | Returns |
|-------|-----------|---------|
| HTTP (who are you) | JWT authentication | `401` |
| HTTP (may you call this) | `[HasPermission(...)]` + `IAsyncAuthorizationFilter` | `403` |
| Data (which rows) | tenant global query filter + scope rules | `404` |

The golden rule: **do not reveal that data exists to someone who cannot see it.**
Out-of-scope rows return `404`, not `403`.

---

## 2. Domain entities

All primary keys are `Guid` (Identity requirement). Auditing timestamps are UTC.

### 2.1 `Tenant`

| Field | Type | Notes |
|-------|------|-------|
| `Id` | `Guid` | PK |
| `Name` | `string` | e.g. "Northwind" |
| `Slug` | `string` | unique, url-safe |

### 2.2 `Department`

| Field | Type | Notes |
|-------|------|-------|
| `Id` | `Guid` | PK |
| `TenantId` | `Guid` | FK -> Tenant |
| `Name` | `string` | unique per tenant |

Unique index: `(TenantId, Name)`.

### 2.3 `ApplicationUser : IdentityUser<Guid>`

| Field | Type | Notes |
|-------|------|-------|
| `Id` | `Guid` | PK (from `IdentityUser<Guid>`) |
| `TenantId` | `Guid` | FK -> Tenant |
| `DepartmentId` | `Guid?` | FK -> Department (nullable, but every seeded demo user has one) |
| `FullName` | `string` | display name |
| `IsActive` | `bool` | deactivated users get **no** permissions |
| `Email` | `string` | unique, confirmed (`IdentityUser` field) |

Unique index on `NormalizedEmail`. Lockout after 5 failed attempts.

### 2.4 `ApplicationRole : IdentityRole<Guid>`

| Field | Type | Notes |
|-------|------|-------|
| `Id` | `Guid` | PK |
| `TenantId` | `Guid` | FK -> Tenant |
| `Name` | `string` | unique **per tenant**, not globally |
| `IsSystemRole` | `bool` | only **Administrator** is a system role: not editable, not deletable. HR / Manager / Employee are seeded defaults that stay editable |
| `Description` | `string?` | |

Unique index: `(TenantId, NormalizedName)` — replaces Identity's global
`RoleNameIndex`. This is what makes "Manager" safe in two tenants.

### 2.5 `Permission`

| Field | Type | Notes |
|-------|------|-------|
| `Id` | `int` | PK (identity) |
| `Code` | `string` | unique, e.g. `leaves.approve` |
| `Group` | `string` | module name for UI grouping: `Leaves`, `Users`, `Roles` |
| `Description` | `string` | human-readable |

Rows are **seeded/synced from the static `Permissions` class** at startup:
insert new codes, keep existing. Never hand-edited.

### 2.6 `RolePermission` (join)

| Field | Type | Notes |
|-------|------|-------|
| `RoleId` | `Guid` | FK -> ApplicationRole |
| `PermissionId` | `int` | FK -> Permission |

Composite PK `(RoleId, PermissionId)`. The Administrator role always holds
every permission.

### 2.7 `RefreshToken`

| Field | Type | Notes |
|-------|------|-------|
| `Id` | `Guid` | PK |
| `UserId` | `Guid` | FK -> ApplicationUser |
| `TokenHash` | `string` | unique; **only the hash is stored**, never the raw token |
| `CreatedAtUtc` | `DateTime` | |
| `ExpiresAtUtc` | `DateTime` | |
| `RevokedAtUtc` | `DateTime?` | set on rotation / logout / reuse detection |
| `ReplacedByHash` | `string?` | chain to the successor token |
| `CreatedByIp` | `string?` | optional diagnostics |

Rotation: each `refresh` marks the used token revoked and issues a new one.
Reusing an already-revoked token revokes **every** session of that user.

### 2.8 `LeaveRequest`

| Field | Type | Notes |
|-------|------|-------|
| `Id` | `Guid` | PK |
| `TenantId` | `Guid` | FK -> Tenant, **global query filter key** |
| `DepartmentId` | `Guid` | FK -> Department (snapshot at creation; drives the department rule) |
| `EmployeeId` | `Guid` | FK -> ApplicationUser (owner) |
| `Type` | `LeaveType` | Annual / Sick / Unpaid |
| `StartDate` | `DateOnly` | |
| `EndDate` | `DateOnly` | must be `>= StartDate` |
| `Reason` | `string` | |
| `Status` | `LeaveStatus` | Pending / Approved / Rejected / Cancelled |
| `ReviewedById` | `Guid?` | FK -> ApplicationUser |
| `ReviewedAtUtc` | `DateTime?` | |
| `CreatedAtUtc` | `DateTime` | |

Indices: `(TenantId, DepartmentId)`, `(EmployeeId, Status)` for scope queries
and overlap checks.

### 2.9 `AuditLog`

| Field | Type | Notes |
|-------|------|-------|
| `Id` | `Guid` | PK |
| `TenantId` | `Guid` | FK -> Tenant; global query filter key |
| `ActorUserId` | `Guid?` | who performed the change |
| `ActorName` | `string` | name snapshot (survives renames) |
| `Action` | `string` | `role.created`, `user.roles.updated`, ... |
| `EntityType` | `string` | `ApplicationRole` / `ApplicationUser` |
| `EntityId` | `Guid?` | affected entity |
| `Details` | `string` | compact JSON before/after snapshot |
| `CreatedAtUtc` | `DateTime` | |

Index: `(TenantId, CreatedAtUtc)`. Read-only; no update/delete endpoints.

### 2.10 Enums

```csharp
enum LeaveType   { Annual, Sick, Unpaid }
enum LeaveStatus { Pending, Approved, Rejected, Cancelled }
```

---

## 3. Permission catalogue

`resource.action`, grouped by module. This is the exact list to implement.

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

---

## 4. Role matrix (default roles, seeded per tenant)

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

Administrator is a **system role**: it always holds every permission and cannot
be edited or deleted. HR / Manager / Employee are seeded defaults per tenant but
remain editable (only *in-use* roles are protected from deletion).

---

## 5. Business rules (cannot be expressed by a permission alone)

1. **Department rule** — a reviewer may approve/reject only requests whose
   `DepartmentId` equals the reviewer's department. Exception: a reviewer who
   also holds `leaves.view.all` (the Administrator) may review any department.
   A manager without `leaves.view.all` cannot even see another department's
   request, so it resolves as `404`, not `403`.
2. **No self-approval** — nobody approves or rejects their own request
   (`EmployeeId == currentUser.Id` -> `403`).
3. **Status rule** — only `Pending` requests can be approved, rejected or
   cancelled. Otherwise `409`.
4. **Approval limit (ABAC)** — a reviewer with `leaves.approve` may decide a
   request of up to 10 calendar days (inclusive). Longer requests require
   `leaves.approve.long` (HR and Administrator) -> otherwise `403`.
5. **Tenant isolation** — data of one company is never visible to another.
   Cross-tenant reads -> `404`.
6. **Overlap rule** — a new request must not overlap an existing
   `Pending`/`Approved` request of the same employee -> `409`.
7. **Date rule** — `EndDate >= StartDate` (data annotation + domain check).

All rules live in **domain methods returning `Result`**, not in controllers.

### Scope resolution for the list endpoint

| `scope` | Required permission | Rows returned |
|---------|--------------------|----------------|
| `own` | `leaves.view.own` | `EmployeeId == me` |
| `team` | `leaves.view.team` | `DepartmentId == my department` |
| `all` | `leaves.view.all` | whole tenant |

---

## 6. API endpoints

Auth cookie: `HttpOnly; Secure; SameSite=Strict`, path `/api/auth`.

| Method | Route | Requires | Notes |
|--------|-------|----------|-------|
| POST | `/api/auth/login` | anonymous | returns access token + sets refresh cookie |
| POST | `/api/auth/refresh` | anonymous (cookie) | rotates refresh token |
| POST | `/api/auth/logout` | anonymous (cookie) | revokes refresh token |
| GET | `/api/me` | signed in | profile + roles + effective permissions |
| GET | `/api/leave-requests?scope=own\|team\|all&status=` | matching `leaves.view.*` | scope-aware list |
| GET | `/api/leave-requests/{id}` | visible in one scope | else `404` |
| POST | `/api/leave-requests` | `leaves.create` | |
| POST | `/api/leave-requests/{id}/cancel` | `leaves.cancel` + owner | |
| POST | `/api/leave-requests/{id}/approve` | `leaves.approve` + not own + (dept unless `leaves.view.all`) + (≤10 days or `leaves.approve.long`) | |
| POST | `/api/leave-requests/{id}/reject` | `leaves.approve` + not own + (dept unless `leaves.view.all`) + (≤10 days or `leaves.approve.long`) | |
| GET | `/api/leave-requests/export` | `leaves.view.all` **AND** `leaves.export` | CSV; two attributes = AND |
| GET | `/api/departments` | `users.view` | department picker for user management |
| GET | `/api/users` | `users.view` | |
| POST | `/api/users` | `users.manage` | |
| PUT | `/api/users/{id}/roles` | `users.manage` | escalation guard |
| POST | `/api/users/{id}/deactivate` | `users.manage` | cannot deactivate self |
| GET | `/api/roles` | `roles.view` | |
| POST | `/api/roles` | `roles.manage` | |
| PUT | `/api/roles/{id}` | `roles.manage` | system roles refused |
| DELETE | `/api/roles/{id}` | `roles.manage` | in-use refused |
| GET | `/api/permissions` | `roles.view` | catalogue for UI grouping |
| GET | `/api/audit-logs?take=` | `audit.view` | read-only, newest first, max 500 |
| WS | `/hubs/notifications` | signed in | pushes `PermissionsChanged` to affected users |

### Error model

All errors use **RFC 9457 problem details** (`application/problem+json`).

| Code | Meaning in this API |
|------|---------------------|
| `400` | validation (data annotations) |
| `401` | wrong credentials, no/expired token, refresh failed — **identical message** for every sign-in failure |
| `403` | authenticated but lacks permission / rule denied (self-approval, >10-day approval without `leaves.approve.long`) |
| `404` | not found **or** outside your tenant/scope |
| `409` | state conflict (already reviewed) / overlap / duplicate name |

---

## 7. Tokens & session

- **Access token**: JWT, **15 minutes**, claims `sub`, `tenant_id`,
  `department_id`, `role`, `email`, `name`. **No permissions in the token.**
- **Refresh token**: 32 random bytes, sent only in the
  `HttpOnly; Secure; SameSite=Strict` cookie scoped to `/api/auth`,
  stored as a hash, rotates on every use.
- Effective permissions are resolved **server-side per request** from the DB
  (`IPermissionService`), cached per user, invalidated on any role/assignment
  change. This is why permissions stay out of the token.
- **SignalR:** the hub uses the same JWT. The browser passes it as the
  `access_token` query parameter (WebSockets cannot set an Authorization
  header); `OnMessageReceived` accepts it only for `/hubs/notifications`.
  After a role/assignment/activation change the API sends `PermissionsChanged`
  to each affected user, and the client re-reads `/api/me`.

---

## 8. Seed data

One demo password for every account (documented in README).

**Northwind** — departments `Sales`, `Finance`

| Department | Users |
|-----------|-------|
| Sales | 1 manager, 2 employees, 1 HR |
| Finance | 1 manager, 2 employees, 1 Administrator |

**Contoso** — department `General` with 1 Administrator, 1 manager and 1
employee.

Each role is seeded **per tenant** with the matrix in section 4. A few leave
requests are seeded in each status (`Pending`, `Approved`, `Rejected`,
`Cancelled`), plus a pending request owned by the Sales manager so the
no-self-approval rule is demonstrable, and an 11-day pending request for
`sales.employee2@northwind.test` so the ABAC approval limit is demonstrable.
HR is seeded with `leaves.approve` and `leaves.approve.long`.

Seeding is idempotent and non-destructive on existing databases. A brand-new
tenant (or a tenant with no users) gets the full seed. An existing tenant with
users keeps its admin edits: only the Administrator system role is topped up to
the full permission catalogue, and demo users whose `DepartmentId` is still
`null` get their seeded department. Deleted or renamed default roles are not
recreated.

---

## 9. Enforcement building blocks

| Concern | Type |
|---------|------|
| Claims access (only place reading claims) | `ICurrentUser` / `CurrentUser` |
| Effective permissions, cached + invalidatable | `IPermissionService` |
| Authorization filter | `[HasPermission("code", ...)]` : `TypeFilterAttribute` -> `PermissionAuthorizationFilter : IAsyncAuthorizationFilter` |
| Anonymous routes | `[AllowAnonymous]` marker |
| Signed-in only routes | `[AuthenticatedOnly]` marker |
| Tenant isolation | EF Core global query filter on `LeaveRequest` + `AuditLog` |
| Change audit | `AuditWriter` appends in the same `SaveChanges` unit of work |
| Live permission refresh | `IUserNotifier` -> SignalR `PermissionsChanged` |

`[HasPermission]` returns `401` when unauthenticated and `403` (problem
details naming the missing permission) when the permission is absent.
Two attributes on one action means **AND**.
