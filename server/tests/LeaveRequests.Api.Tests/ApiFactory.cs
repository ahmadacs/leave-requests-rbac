using System.Net.Http.Headers;
using System.Net.Http.Json;
using LeaveRequests.Api.Contracts.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;

namespace LeaveRequests.Api.Tests;

public sealed class ApiFactory : WebApplicationFactory<Program>
{
    public const string DemoPassword = "Passw0rd!";
    public const string RefreshCookieName = "refresh_token";

    private const string SigningKey = "leave-requests-integration-tests-signing-key-32+";

    private readonly string _databaseName = $"LeaveRequestsTests_{Guid.NewGuid():N}";
    private readonly string _connectionString;

    public ApiFactory()
    {
        var dataSource = Environment.GetEnvironmentVariable("LEAVE_REQUESTS_TEST_SQLSERVER")
            ?? @".\SQLEXPRESS";

        _connectionString = new SqlConnectionStringBuilder
        {
            DataSource = dataSource,
            InitialCatalog = _databaseName,
            IntegratedSecurity = true,
            TrustServerCertificate = true,
            MultipleActiveResultSets = true
        }.ConnectionString;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", _connectionString);
        builder.UseSetting("Jwt:SigningKey", SigningKey);
        builder.UseSetting("Jwt:Issuer", "LeaveRequests.Tests");
        builder.UseSetting("Jwt:Audience", "LeaveRequests.Tests.Client");
        builder.UseSetting("Jwt:AccessTokenMinutes", "60");
        builder.UseSetting("Jwt:RefreshTokenDays", "7");
        builder.UseSetting("Jwt:RefreshCookieName", RefreshCookieName);
        builder.UseSetting("Jwt:RefreshCookiePath", "/api/auth");
    }

    public HttpClient CreateApiClient(bool handleCookies = false) =>
        CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = handleCookies
        });

    public async Task<ApiSession> LoginAsync(string email, bool handleCookies = false)
    {
        var client = CreateApiClient(handleCookies);
        var response = await client.PostAsJsonAsync("/api/auth/login", new
        {
            Email = email,
            Password = DemoPassword
        });

        response.EnsureSuccessStatusCode();

        var auth = await response.Content.ReadFromJsonAsync<AuthResponse>()
            ?? throw new InvalidOperationException("The login response did not contain a session.");

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", auth.AccessToken);

        return new ApiSession(client, auth, response);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (!disposing)
        {
            return;
        }

        SqlConnection.ClearAllPools();

        try
        {
            var master = new SqlConnectionStringBuilder(_connectionString)
            {
                InitialCatalog = "master"
            }.ConnectionString;

            using var connection = new SqlConnection(master);
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText =
                $"IF DB_ID(N'{_databaseName}') IS NOT NULL " +
                $"BEGIN ALTER DATABASE [{_databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; " +
                $"DROP DATABASE [{_databaseName}]; END";
            command.ExecuteNonQuery();
        }
        catch (SqlException)
        {
            // Best effort cleanup; the unique database is disposable test data.
        }
    }
}

public sealed class ApiSession(HttpClient client, AuthResponse auth, HttpResponseMessage loginResponse)
    : IDisposable
{
    public HttpClient Client { get; } = client;
    public AuthResponse Auth { get; } = auth;
    public HttpResponseMessage LoginResponse { get; } = loginResponse;

    public void Dispose() => Client.Dispose();
}
