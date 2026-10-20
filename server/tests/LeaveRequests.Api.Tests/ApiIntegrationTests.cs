using System.Net;
using System.Net.Http.Json;
using LeaveRequests.Api.Audit;
using LeaveRequests.Api.Contracts.Audit;
using LeaveRequests.Api.Contracts.Auth;
using LeaveRequests.Api.Contracts.Departments;
using LeaveRequests.Api.Contracts.LeaveRequests;
using LeaveRequests.Api.Contracts.Roles;
using LeaveRequests.Api.Contracts.Users;
using LeaveRequests.Api.Domain.Entities;
using LeaveRequests.Api.Security;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Xunit;

namespace LeaveRequests.Api.Tests;

public sealed class ApiIntegrationTests
{
    [Fact]
    public async Task Login_IssuesRoleClaim_AndMeReturnsEffectivePermissions()
    {
        using var factory = new ApiFactory();
        using var session = await factory.LoginAsync("admin@northwind.test");

        var payload = TestHttp.ReadJwtPayload(session.Auth.AccessToken);
        Assert.Contains(RoleNames.Administrator, TestHttp.ReadRoles(payload));

        var me = await (await session.Client.GetAsync("/api/me")).ReadJsonAsync<MeResponse>();

        Assert.NotNull(me);
        Assert.Contains(RoleNames.Administrator, me.Roles);
        Assert.Contains(Permissions.Leaves.ViewAll, me.Permissions);
        Assert.Contains(Permissions.Users.Manage, me.Permissions);
    }

    [Fact]
    public async Task Me_RequiresAuthentication()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateApiClient();

        var response = await client.GetAsync("/api/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AllScope_RequiresViewAllPermission()
    {
        using var factory = new ApiFactory();
        using var employee = await factory.LoginAsync("sales.employee1@northwind.test");
        using var manager = await factory.LoginAsync("sales.manager@northwind.test");
        using var admin = await factory.LoginAsync("admin@northwind.test");

        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await employee.Client.GetAsync("/api/leave-requests?scope=all")).StatusCode);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await manager.Client.GetAsync("/api/leave-requests?scope=all")).StatusCode);

        var adminResponse = await admin.Client.GetAsync("/api/leave-requests?scope=all");
        Assert.Equal(HttpStatusCode.OK, adminResponse.StatusCode);
        Assert.NotEmpty(await adminResponse.ReadJsonAsync<List<LeaveRequestResponse>>() ?? []);
    }

    [Fact]
    public async Task Manager_CannotSeeRequestFromAnotherDepartment_ButAdminCan()
    {
        using var factory = new ApiFactory();
        using var admin = await factory.LoginAsync("admin@northwind.test");
        using var manager = await factory.LoginAsync("sales.manager@northwind.test");

        var financeRequest = (await GetAllRequestsAsync(admin.Client))
            .First(request => request.DepartmentName == "Finance");

        var managerResponse = await manager.Client.GetAsync($"/api/leave-requests/{financeRequest.Id}");
        Assert.Equal(HttpStatusCode.NotFound, managerResponse.StatusCode);

        var adminResponse = await admin.Client.GetAsync($"/api/leave-requests/{financeRequest.Id}");
        Assert.Equal(HttpStatusCode.OK, adminResponse.StatusCode);
    }

    [Fact]
    public async Task Administrator_CanApproveAcrossDepartments()
    {
        using var factory = new ApiFactory();
        using var admin = await factory.LoginAsync("admin@northwind.test");

        var request = (await GetAllRequestsAsync(admin.Client)).First(item =>
            item.Status == LeaveStatus.Pending
            && item.DepartmentId != admin.Auth.User.DepartmentId);

        var response = await admin.Client.PostAsync($"/api/leave-requests/{request.Id}/approve", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.ReadJsonAsync<LeaveRequestResponse>();
        Assert.NotNull(updated);
        Assert.Equal(LeaveStatus.Approved, updated.Status);
        Assert.Equal(admin.Auth.User.Id, updated.ReviewedById);
    }

    [Fact]
    public async Task Manager_CannotApproveOwnRequest()
    {
        using var factory = new ApiFactory();
        using var manager = await factory.LoginAsync("sales.manager@northwind.test");

        var ownRequests = await (await manager.Client.GetAsync("/api/leave-requests?scope=own"))
            .ReadJsonAsync<List<LeaveRequestResponse>>();
        var pending = Assert.Single(ownRequests!, request => request.Status == LeaveStatus.Pending);

        var response = await manager.Client.PostAsync($"/api/leave-requests/{pending.Id}/approve", null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ManagerCannotApproveLongRequest_ButHrCan()
    {
        using var factory = new ApiFactory();
        using var manager = await factory.LoginAsync("sales.manager@northwind.test");
        using var hr = await factory.LoginAsync("hr@northwind.test");

        var teamRequests = await (await manager.Client.GetAsync("/api/leave-requests?scope=team"))
            .ReadJsonAsync<List<LeaveRequestResponse>>();
        var longRequest = Assert.Single(
            teamRequests!,
            request =>
                request.Status == LeaveStatus.Pending
                && request.EmployeeId != manager.Auth.User.Id
                && request.EndDate.DayNumber - request.StartDate.DayNumber + 1 > 10);

        var managerResponse = await manager.Client.PostAsync(
            $"/api/leave-requests/{longRequest.Id}/approve",
            null);
        Assert.Equal(HttpStatusCode.Forbidden, managerResponse.StatusCode);

        var hrResponse = await hr.Client.PostAsync($"/api/leave-requests/{longRequest.Id}/approve", null);
        Assert.Equal(HttpStatusCode.OK, hrResponse.StatusCode);
        var approved = await hrResponse.ReadJsonAsync<LeaveRequestResponse>();
        Assert.NotNull(approved);
        Assert.Equal(LeaveStatus.Approved, approved.Status);
    }

    [Fact]
    public async Task Create_RejectsOverlappingRequest()
    {
        using var factory = new ApiFactory();
        using var employee = await factory.LoginAsync("sales.employee1@northwind.test");

        var response = await employee.Client.PostJsonAsync("/api/leave-requests", new CreateLeaveRequestRequest
        {
            Type = LeaveType.Annual,
            StartDate = new DateOnly(2026, 11, 2),
            EndDate = new DateOnly(2026, 11, 6),
            Reason = "Overlapping request"
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Create_RejectsEndDateBeforeStartDate()
    {
        using var factory = new ApiFactory();
        using var employee = await factory.LoginAsync("sales.employee1@northwind.test");

        var response = await employee.Client.PostJsonAsync("/api/leave-requests", new CreateLeaveRequestRequest
        {
            Type = LeaveType.Annual,
            StartDate = new DateOnly(2026, 11, 10),
            EndDate = new DateOnly(2026, 11, 9),
            Reason = "Invalid range"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Export_RequiresExportAndViewAllPermissions_AndReturnsCsv()
    {
        using var factory = new ApiFactory();
        using var employee = await factory.LoginAsync("sales.employee1@northwind.test");
        using var admin = await factory.LoginAsync("admin@northwind.test");

        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await employee.Client.GetAsync("/api/leave-requests/export")).StatusCode);

        var response = await admin.Client.GetAsync("/api/leave-requests/export");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);
        var csv = await response.Content.ReadAsStringAsync();
        Assert.StartsWith("Id,Employee,Department,Type,StartDate,EndDate,Status,Reason", csv);
    }

    [Fact]
    public async Task CrossTenantRequest_IsHiddenFromOtherTenantAdministrator()
    {
        using var factory = new ApiFactory();
        using var northwindAdmin = await factory.LoginAsync("admin@northwind.test");
        using var contosoAdmin = await factory.LoginAsync("admin@contoso.test");

        var contosoRequest = (await GetAllRequestsAsync(contosoAdmin.Client)).First();

        var response = await northwindAdmin.Client.GetAsync($"/api/leave-requests/{contosoRequest.Id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Departments_RequireUsersViewPermission()
    {
        using var factory = new ApiFactory();
        using var manager = await factory.LoginAsync("sales.manager@northwind.test");
        using var admin = await factory.LoginAsync("admin@northwind.test");

        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await manager.Client.GetAsync("/api/departments")).StatusCode);

        var response = await admin.Client.GetAsync("/api/departments");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var departments = await response.ReadJsonAsync<List<DepartmentResponse>>();
        Assert.NotNull(departments);
        Assert.Contains(departments, department => department.Name == "Finance");
        Assert.Contains(departments, department => department.Name == "Sales");
    }

    [Fact]
    public async Task SystemRole_CannotBeEdited()
    {
        using var factory = new ApiFactory();
        using var admin = await factory.LoginAsync("admin@northwind.test");

        var roles = await (await admin.Client.GetAsync("/api/roles")).ReadJsonAsync<List<RoleResponse>>();
        var administrator = Assert.Single(roles!, role => role.IsSystemRole);

        var response = await admin.Client.PutAsJsonAsync(
            $"/api/roles/{administrator.Id}",
            new UpdateRoleRequest
            {
                Name = administrator.Name,
                Permissions = administrator.Permissions
            },
            TestHttp.Json);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CreateUser_AcceptsMissingRoles()
    {
        using var factory = new ApiFactory();
        using var admin = await factory.LoginAsync("admin@northwind.test");

        var response = await admin.Client.PostJsonAsync("/api/users", new CreateUserRequest
        {
            Email = "no.roles@northwind.test",
            FullName = "No Roles",
            Roles = null
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var user = await response.ReadJsonAsync<UserResponse>();
        Assert.NotNull(user);
        Assert.Empty(user.Roles);
    }

    [Fact]
    public async Task RefreshToken_Rotates_AndReuseRevokesTheWholeFamily()
    {
        using var factory = new ApiFactory();
        using var session = await factory.LoginAsync("admin@northwind.test", handleCookies: true);

        var firstToken = TestHttp.ReadCookie(session.LoginResponse);
        Assert.NotNull(firstToken);

        var firstRefresh = await session.Client.PostAsync("/api/auth/refresh", null);
        Assert.Equal(HttpStatusCode.OK, firstRefresh.StatusCode);
        var secondToken = TestHttp.ReadCookie(firstRefresh);
        Assert.NotNull(secondToken);
        Assert.NotEqual(firstToken, secondToken);

        using var replayClient = factory.CreateApiClient();
        var replayRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh")
            .WithCookie(firstToken!);
        var replay = await replayClient.SendAsync(replayRequest);
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);

        using var rotatedClient = factory.CreateApiClient();
        var rotatedRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh")
            .WithCookie(secondToken!);
        var rotated = await rotatedClient.SendAsync(rotatedRequest);
        Assert.Equal(HttpStatusCode.Unauthorized, rotated.StatusCode);
    }

    [Fact]
    public async Task PermissionCache_IsInvalidatedWhenARoleChanges()
    {
        using var factory = new ApiFactory();
        using var admin = await factory.LoginAsync("admin@northwind.test");

        var roleName = $"Cache Test {Guid.NewGuid():N}";
        var createRole = await admin.Client.PostJsonAsync("/api/roles", new CreateRoleRequest
        {
            Name = roleName,
            Permissions = [Permissions.Leaves.ViewOwn]
        });
        createRole.EnsureSuccessStatusCode();
        var role = await createRole.ReadJsonAsync<RoleResponse>();
        Assert.NotNull(role);

        var email = $"{Guid.NewGuid():N}@northwind.test";
        var createUser = await admin.Client.PostJsonAsync("/api/users", new CreateUserRequest
        {
            Email = email,
            FullName = "Cache Test User",
            Roles = [roleName]
        });
        createUser.EnsureSuccessStatusCode();

        using var user = await factory.LoginAsync(email);
        var before = await (await user.Client.GetAsync("/api/me")).ReadJsonAsync<MeResponse>();
        Assert.NotNull(before);
        Assert.Contains(Permissions.Leaves.ViewOwn, before.Permissions);

        var updateRole = await admin.Client.PutAsJsonAsync(
            $"/api/roles/{role.Id}",
            new UpdateRoleRequest
            {
                Name = roleName,
                Permissions = []
            },
            TestHttp.Json);
        updateRole.EnsureSuccessStatusCode();

        var after = await (await user.Client.GetAsync("/api/me")).ReadJsonAsync<MeResponse>();
        Assert.NotNull(after);
        Assert.DoesNotContain(Permissions.Leaves.ViewOwn, after.Permissions);
    }

    [Fact]
    public async Task AuditLog_RecordsRoleAndAssignmentChanges_AndRequiresPermission()
    {
        using var factory = new ApiFactory();
        using var admin = await factory.LoginAsync("admin@northwind.test");
        using var employee = await factory.LoginAsync("sales.employee1@northwind.test");

        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await employee.Client.GetAsync("/api/audit-logs")).StatusCode);

        var roleName = $"Audited Role {Guid.NewGuid():N}";
        var createRole = await admin.Client.PostJsonAsync("/api/roles", new CreateRoleRequest
        {
            Name = roleName,
            Permissions = [Permissions.Leaves.ViewOwn]
        });
        createRole.EnsureSuccessStatusCode();
        var role = await createRole.ReadJsonAsync<RoleResponse>();
        Assert.NotNull(role);

        var email = $"{Guid.NewGuid():N}@northwind.test";
        var createUser = await admin.Client.PostJsonAsync("/api/users", new CreateUserRequest
        {
            Email = email,
            FullName = "Audited User",
            Roles = [roleName]
        });
        createUser.EnsureSuccessStatusCode();
        var user = await createUser.ReadJsonAsync<UserResponse>();
        Assert.NotNull(user);

        var setRoles = await admin.Client.PutAsJsonAsync(
            $"/api/users/{user.Id}/roles",
            new UpdateUserRolesRequest { Roles = [] },
            TestHttp.Json);
        setRoles.EnsureSuccessStatusCode();

        var response = await admin.Client.GetAsync("/api/audit-logs");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var entries = await response.ReadJsonAsync<List<AuditLogResponse>>();
        Assert.NotNull(entries);

        Assert.Contains(entries, entry =>
            entry.Action == AuditActions.RoleCreated
            && entry.EntityId == role.Id);
        Assert.Contains(entries, entry =>
            entry.Action == AuditActions.UserCreated
            && entry.EntityId == user.Id);
        Assert.Contains(entries, entry =>
            entry.Action == AuditActions.UserRolesUpdated
            && entry.EntityId == user.Id);
    }

    [Fact]
    public async Task RoleChange_PushesPermissionsChangedOverSignalR()
    {
        using var factory = new ApiFactory();
        using var admin = await factory.LoginAsync("admin@northwind.test");

        var roleName = $"Realtime Role {Guid.NewGuid():N}";
        var createRole = await admin.Client.PostJsonAsync("/api/roles", new CreateRoleRequest
        {
            Name = roleName,
            Permissions = [Permissions.Leaves.ViewOwn]
        });
        createRole.EnsureSuccessStatusCode();
        var role = await createRole.ReadJsonAsync<RoleResponse>();
        Assert.NotNull(role);

        var email = $"{Guid.NewGuid():N}@northwind.test";
        var createUser = await admin.Client.PostJsonAsync("/api/users", new CreateUserRequest
        {
            Email = email,
            FullName = "Realtime User",
            Roles = [roleName]
        });
        createUser.EnsureSuccessStatusCode();

        using var user = await factory.LoginAsync(email);

        await using var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(factory.Server.BaseAddress, "/hubs/notifications"), options =>
            {
                options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                options.AccessTokenProvider = () => Task.FromResult<string?>(user.Auth.AccessToken);
                options.Transports = HttpTransportType.LongPolling;
            })
            .Build();

        var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.On("PermissionsChanged", () => changed.TrySetResult());

        await connection.StartAsync();

        var updateRole = await admin.Client.PutAsJsonAsync(
            $"/api/roles/{role.Id}",
            new UpdateRoleRequest
            {
                Name = roleName,
                Permissions = []
            },
            TestHttp.Json);
        updateRole.EnsureSuccessStatusCode();

        var completed = await Task.WhenAny(changed.Task, Task.Delay(TimeSpan.FromSeconds(10)));
        Assert.Same(changed.Task, completed);
    }

    private static async Task<List<LeaveRequestResponse>> GetAllRequestsAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/leave-requests?scope=all");
        response.EnsureSuccessStatusCode();
        return await response.ReadJsonAsync<List<LeaveRequestResponse>>() ?? [];
    }
}
