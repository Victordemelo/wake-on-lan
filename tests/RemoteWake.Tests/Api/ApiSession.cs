using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using RemoteWake.Api.Contracts;
using RemoteWake.Api.Services;

namespace RemoteWake.Tests.Api;

// A signed-in user of the API, as the web app would be: session cookie plus the
// anti-CSRF header on every request.
public sealed class ApiSession(HttpClient http, UserResponse user)
{
    public const string DefaultPassword = "Senha-forte-123";

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public HttpClient Http { get; } = http;
    public UserResponse User { get; } = user;

    public static string NewEmail() => $"user-{Guid.NewGuid():N}@example.test";

    // A browser client without a session: keeps cookies and sends the anti-CSRF header.
    public static HttpClient Anonymous(ApiFactory api)
    {
        var http = api.CreateClient();
        http.DefaultRequestHeaders.Add(CsrfGuard.Header, "1");
        return http;
    }

    public static async Task<ApiSession> RegisterAsync(ApiFactory api, string? email = null, string password = DefaultPassword)
    {
        var http = Anonymous(api);
        var response = await http.PostAsJsonAsync("/api/auth/register", new
        {
            name = "Usuário de teste",
            email = email ?? NewEmail(),
            password,
            setupToken = ApiFactory.SetupToken
        }, TestContext.Current.CancellationToken);
        return await SignedInAsync(http, response);
    }

    public static async Task<ApiSession> LoginAsync(ApiFactory api, string email, string password, string? code = null)
    {
        var http = Anonymous(api);
        var response = await http.PostAsJsonAsync("/api/auth/login", new { email, password, code }, TestContext.Current.CancellationToken);
        return await SignedInAsync(http, response);
    }

    private static async Task<ApiSession> SignedInAsync(HttpClient http, HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        var auth = await response.Content.ReadFromJsonAsync<AuthResponse>(Json, TestContext.Current.CancellationToken);
        return new ApiSession(http, auth!.User);
    }

    public static object Machine(string name = "PC de teste", string destination = "192.168.1.255",
        string method = "LocalBroadcast", string mac = "02:00:00:00:00:01") =>
        new { name, macAddress = mac, hostname = "pc-teste", broadcastAddress = destination, wolPort = 9, wakeMethod = method };

    public async Task<MachineResponse> CreateMachineAsync(object? machine = null)
    {
        var response = await Http.PostAsJsonAsync("/api/machines", machine ?? Machine(), TestContext.Current.CancellationToken);
        Assert.Equal(System.Net.HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<MachineResponse>(Json, TestContext.Current.CancellationToken))!;
    }

    public async Task<T> GetAsync<T>(string path) =>
        (await Http.GetFromJsonAsync<T>(path, Json, TestContext.Current.CancellationToken))!;
}
