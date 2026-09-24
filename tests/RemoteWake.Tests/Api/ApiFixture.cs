using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using RemoteWake.Api.Services;

namespace RemoteWake.Tests.Api;

// Creates a throwaway database on the server given by REMOTE_WAKE_TEST_POSTGRES.
public sealed class PostgresDatabase : IAsyncLifetime
{
    public const string Variable = "REMOTE_WAKE_TEST_POSTGRES";
    private static readonly string? Server = Environment.GetEnvironmentVariable(Variable);
    private readonly string name = "rw_test_" + Guid.NewGuid().ToString("N")[..12];

    public static bool Available => !string.IsNullOrWhiteSpace(Server);
    public string ConnectionString { get; private set; } = string.Empty;

    public static void SkipIfUnavailable() =>
        Assert.SkipUnless(Available, $"Defina {Variable} (ex.: Host=localhost;Username=postgres;Password=...) para rodar os testes com PostgreSQL.");

    public async ValueTask InitializeAsync()
    {
        if (!Available) return;
        await ExecuteOnServerAsync($"CREATE DATABASE \"{name}\"");
        ConnectionString = new NpgsqlConnectionStringBuilder(Server) { Database = name }.ConnectionString;
    }

    public async Task ExecuteAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<T?> ScalarAsync<T>(string sql)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        var value = await command.ExecuteScalarAsync();
        return value is null or DBNull ? default : (T)value;
    }

    public async ValueTask DisposeAsync()
    {
        if (!Available) return;
        NpgsqlConnection.ClearAllPools();
        await ExecuteOnServerAsync($"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE)");
    }

    private static async Task ExecuteOnServerAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(Server);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}

public sealed class ApiFactory(string connectionString, IReadOnlyDictionary<string, string?> overrides) : WebApplicationFactory<Program>
{
    public const string GatewayKey = "test-gateway-key";
    public const string GatewayOwner = "gateway.owner@example.test";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Database"] = connectionString,
            ["Jwt:Key"] = "test-only-signing-key-long-enough-for-hmac-sha256",
            ["Jwt:Issuer"] = "RemoteWake",
            ["Jwt:Audience"] = "RemoteWake.Web",
            ["Gateway:Key"] = GatewayKey,
            ["Gateway:OwnerEmail"] = GatewayOwner,
            ["Agent:MasterKey"] = "test-agent-master-key",
            ["Registration:Open"] = "true",
            ["RateLimits:AuthPerMinute"] = "1000",
            ["ForwardedHeaders:TrustedNetworks:0"] = "172.16.0.0/12"
        };
        foreach (var (key, value) in overrides) settings[key] = value;
        foreach (var (key, value) in settings) builder.UseSetting(key, value);

        builder.ConfigureServices(services =>
        {
            services.AddSingleton<IStartupFilter, TestPeerStartupFilter>();
            // Short long-poll so tests that exercise workers do not wait 20 seconds.
            services.AddSingleton(new RemoteJobBrokerOptions { PollTimeout = TimeSpan.FromMilliseconds(300) });
        });
    }
}

// TestServer has no network peer; tests pick the address the API sees through a header.
internal sealed class TestPeerStartupFilter : IStartupFilter
{
    public const string Header = "X-Test-Peer";

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use((context, nextMiddleware) =>
        {
            var peer = context.Request.Headers[Header].FirstOrDefault();
            context.Connection.RemoteIpAddress = IPAddress.Parse(peer ?? "198.51.100.1");
            return nextMiddleware(context);
        });
        next(app);
    };
}

public abstract class ApiFixture : IAsyncLifetime
{
    public PostgresDatabase Database { get; } = new();
    public ApiFactory Api { get; private set; } = null!;
    protected virtual IReadOnlyDictionary<string, string?> Settings => new Dictionary<string, string?>();

    public async ValueTask InitializeAsync()
    {
        await Database.InitializeAsync();
        if (PostgresDatabase.Available) Api = new ApiFactory(Database.ConnectionString, Settings);
    }

    public async ValueTask DisposeAsync()
    {
        if (Api is not null) await Api.DisposeAsync();
        await Database.DisposeAsync();
    }
}

public sealed class DefaultApi : ApiFixture;

public sealed class ClosedRegistrationApi : ApiFixture
{
    protected override IReadOnlyDictionary<string, string?> Settings =>
        new Dictionary<string, string?> { ["Registration:Open"] = "false" };
}

public sealed class StrictRateLimitApi : ApiFixture
{
    protected override IReadOnlyDictionary<string, string?> Settings =>
        new Dictionary<string, string?> { ["RateLimits:AuthPerMinute"] = "2" };
}
