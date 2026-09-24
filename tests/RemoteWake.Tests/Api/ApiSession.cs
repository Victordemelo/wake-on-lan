using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using RemoteWake.Api.Contracts;

namespace RemoteWake.Tests.Api;

// An authenticated user of the API, as the web app would be.
public sealed class ApiSession(HttpClient http, UserResponse user)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public HttpClient Http { get; } = http;
    public UserResponse User { get; } = user;

    public static string NewEmail() => $"user-{Guid.NewGuid():N}@example.test";

    public static async Task<ApiSession> RegisterAsync(ApiFactory api, string? email = null, string password = "Senha-forte-123")
    {
        var http = api.CreateClient();
        var response = await http.PostAsJsonAsync("/api/auth/register",
            new { name = "Usuário de teste", email = email ?? NewEmail(), password }, TestContext.Current.CancellationToken);
        return await AuthenticateAsync(http, response);
    }

    public static async Task<ApiSession> LoginAsync(ApiFactory api, string email, string password)
    {
        var http = api.CreateClient();
        var response = await http.PostAsJsonAsync("/api/auth/login", new { email, password }, TestContext.Current.CancellationToken);
        return await AuthenticateAsync(http, response);
    }

    private static async Task<ApiSession> AuthenticateAsync(HttpClient http, HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        var auth = await response.Content.ReadFromJsonAsync<AuthResponse>(Json, TestContext.Current.CancellationToken);
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.Token);
        return new ApiSession(http, auth.User);
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
