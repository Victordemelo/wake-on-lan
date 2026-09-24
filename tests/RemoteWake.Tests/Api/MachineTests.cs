using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using RemoteWake.Api.Contracts;

namespace RemoteWake.Tests.Api;

internal sealed record ActivityItem(Guid Id, Guid? MachineId, string MachineName, string Action, bool Succeeded, string Message);
internal sealed record AgentKey(Guid MachineId, string Key);

public sealed class MachineTests(DefaultApi fixture) : IClassFixture<DefaultApi>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Machines_are_created_listed_updated_and_deleted()
    {
        PostgresDatabase.SkipIfUnavailable();
        var session = await ApiSession.RegisterAsync(fixture.Api);

        var created = await session.CreateMachineAsync(ApiSession.Machine(mac: "aa-bb-cc-dd-ee-ff"));
        Assert.Equal("AABBCCDDEEFF", created.MacAddress);

        var update = await session.Http.PutAsJsonAsync($"/api/machines/{created.Id}", ApiSession.Machine(name: "PC renomeado"), Ct);
        update.EnsureSuccessStatusCode();
        var listed = Assert.Single(await session.GetAsync<List<MachineResponse>>("/api/machines"));
        Assert.Equal("PC renomeado", listed.Name);

        Assert.Equal(HttpStatusCode.NoContent, (await session.Http.DeleteAsync($"/api/machines/{created.Id}", Ct)).StatusCode);
        Assert.Empty(await session.GetAsync<List<MachineResponse>>("/api/machines"));
    }

    [Fact]
    public async Task Users_cannot_see_or_control_each_others_machines()
    {
        PostgresDatabase.SkipIfUnavailable();
        var owner = await ApiSession.RegisterAsync(fixture.Api);
        var other = await ApiSession.RegisterAsync(fixture.Api);
        var machine = await owner.CreateMachineAsync(ApiSession.Machine(destination: "127.0.0.1"));
        (await owner.Http.PostAsync($"/api/machines/{machine.Id}/wake", null, Ct)).EnsureSuccessStatusCode();

        Assert.Empty(await other.GetAsync<List<MachineResponse>>("/api/machines"));
        Assert.Empty(await other.GetAsync<List<ActivityItem>>("/api/activity"));
        Assert.Equal(HttpStatusCode.NotFound, (await other.Http.PutAsJsonAsync($"/api/machines/{machine.Id}", ApiSession.Machine(), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.Http.DeleteAsync($"/api/machines/{machine.Id}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.Http.PostAsync($"/api/machines/{machine.Id}/wake", null, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.Http.PostAsync($"/api/machines/{machine.Id}/agent-key", null, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await other.Http.PostAsJsonAsync($"/api/machines/{machine.Id}/actions", new { action = "shutdown" }, Ct)).StatusCode);
        Assert.Single(await owner.GetAsync<List<MachineResponse>>("/api/machines"));
    }

    [Fact]
    public async Task Invalid_machines_get_a_portuguese_explanation()
    {
        PostgresDatabase.SkipIfUnavailable();
        var session = await ApiSession.RegisterAsync(fixture.Api);

        var response = await session.Http.PostAsJsonAsync("/api/machines", ApiSession.Machine(mac: "AA:BB"), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Contains("endereço MAC", body.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Direct_wake_is_sent_and_recorded()
    {
        PostgresDatabase.SkipIfUnavailable();
        var session = await ApiSession.RegisterAsync(fixture.Api);
        var machine = await session.CreateMachineAsync(ApiSession.Machine(destination: "127.0.0.1"));

        var wake = await session.Http.PostAsync($"/api/machines/{machine.Id}/wake", null, Ct);

        wake.EnsureSuccessStatusCode();
        var attempt = Assert.Single(await session.GetAsync<List<ActivityItem>>("/api/activity"));
        Assert.Equal(("wake", true), (attempt.Action, attempt.Succeeded));
        Assert.NotNull(Assert.Single(await session.GetAsync<List<MachineResponse>>("/api/machines")).LastWakeRequestedAt);
    }

    [Fact]
    public async Task Power_actions_need_an_online_agent_and_arbitrary_actions_are_refused()
    {
        PostgresDatabase.SkipIfUnavailable();
        var session = await ApiSession.RegisterAsync(fixture.Api);
        var machine = await session.CreateMachineAsync();

        var offline = await session.Http.PostAsJsonAsync($"/api/machines/{machine.Id}/actions", new { action = "shutdown" }, Ct);
        var arbitrary = await session.Http.PostAsJsonAsync($"/api/machines/{machine.Id}/actions", new { action = "rm -rf /" }, Ct);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, offline.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, arbitrary.StatusCode);
        var attempt = Assert.Single(await session.GetAsync<List<ActivityItem>>("/api/activity"));
        Assert.Equal(("shutdown", false), (attempt.Action, attempt.Succeeded));
    }
}
