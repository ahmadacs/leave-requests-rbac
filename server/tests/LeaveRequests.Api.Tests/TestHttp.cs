using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Xunit;

namespace LeaveRequests.Api.Tests;

internal static class TestHttp
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public static Task<T?> ReadJsonAsync<T>(this HttpResponseMessage response) =>
        response.Content.ReadFromJsonAsync<T>(Json);

    public static Task<HttpResponseMessage> PostJsonAsync<T>(
        this HttpClient client,
        string requestUri,
        T value) =>
        client.PostAsJsonAsync(requestUri, value, Json);
    public static string? ReadCookie(HttpResponseMessage response, string name = ApiFactory.RefreshCookieName)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var headers))
        {
            return null;
        }

        foreach (var header in headers)
        {
            var pair = header.Split(';', 2)[0];
            var separator = pair.IndexOf('=');
            if (separator > 0
                && pair[..separator].Equals(name, StringComparison.Ordinal))
            {
                return pair[(separator + 1)..];
            }
        }

        return null;
    }

    public static HttpRequestMessage WithCookie(
        this HttpRequestMessage request,
        string value,
        string name = ApiFactory.RefreshCookieName)
    {
        request.Headers.Add("Cookie", $"{name}={value}");
        return request;
    }

    public static JsonElement ReadJwtPayload(string accessToken)
    {
        var parts = accessToken.Split('.');
        Assert.Equal(3, parts.Length);

        var payload = parts[1]
            .Replace('-', '+')
            .Replace('_', '/');

        payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');

        using var document = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(payload)));
        return document.RootElement.Clone();
    }

    public static IEnumerable<string> ReadRoles(JsonElement payload)
    {
        if (!payload.TryGetProperty("role", out var role))
        {
            return [];
        }

        return role.ValueKind == JsonValueKind.Array
            ? role.EnumerateArray().Select(item => item.GetString()!).ToArray()
            : [role.GetString()!];
    }
}
