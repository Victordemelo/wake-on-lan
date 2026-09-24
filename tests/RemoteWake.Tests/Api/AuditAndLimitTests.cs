using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace RemoteWake.Tests.Api;

public sealed class AuditTests(DefaultApi fixture) : IClassFixture<DefaultApi>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task History_outlives_a_removed_machine()
    {
        PostgresDatabase.SkipIfUnavailable();
        var session = await ApiSession.RegisterAsync(fixture.Api);
        var machine = await session.CreateMachineAsync(ApiSession.Machine(name: "PC antigo", destination: "127.0.0.1"));
        (await session.Http.PostAsync($"/api/machines/{machine.Id}/wake", null, Ct)).EnsureSuccessStatusCode();

        (await session.Http.DeleteAsync($"/api/machines/{machine.Id}", Ct)).EnsureSuccessStatusCode();

        var attempt = Assert.Single(await session.GetAsync<List<ActivityItem>>("/api/activity"));
        Assert.Null(attempt.MachineId);
        Assert.Equal(("PC antigo", "wake", true), (attempt.MachineName, attempt.Action, attempt.Succeeded));
    }
}

// The fixture allows two wake or power commands per account per minute.
public sealed class CommandLimitTests(StrictRateLimitApi fixture) : IClassFixture<StrictRateLimitApi>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Each_account_has_its_own_command_limit()
    {
        PostgresDatabase.SkipIfUnavailable();
        var first = await ApiSession.RegisterAsync(fixture.Api);
        var second = await ApiSession.RegisterAsync(fixture.Api);
        var machine = await first.CreateMachineAsync(ApiSession.Machine(destination: "127.0.0.1"));
        var otherMachine = await second.CreateMachineAsync(ApiSession.Machine(destination: "127.0.0.1"));

        (await first.Http.PostAsync($"/api/machines/{machine.Id}/wake", null, Ct)).EnsureSuccessStatusCode();
        (await first.Http.PostAsync($"/api/machines/{machine.Id}/wake", null, Ct)).EnsureSuccessStatusCode();
        var limited = await first.Http.PostAsync($"/api/machines/{machine.Id}/wake", null, Ct);

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Contains("Aguarde", (await limited.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("message").GetString());
        (await second.Http.PostAsync($"/api/machines/{otherMachine.Id}/wake", null, Ct)).EnsureSuccessStatusCode();
    }
}
