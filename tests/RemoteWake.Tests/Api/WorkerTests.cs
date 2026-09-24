using System.Net;
using System.Net.Http.Json;
using RemoteWake.Api.Contracts;
using RemoteWake.Shared;

namespace RemoteWake.Tests.Api;

public sealed class WorkerTests(DefaultApi fixture) : IClassFixture<DefaultApi>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient Worker(string key)
    {
        var client = fixture.Api.CreateClient();
        client.DefaultRequestHeaders.Add("X-Remote-Wake-Key", key);
        return client;
    }

    // Polls like a real worker until a job arrives, then reports the result.
    private static async Task<RemoteJob> ServeOneJobAsync(HttpClient worker, string basePath, RemoteJobResult result)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            var poll = await worker.GetAsync($"{basePath}/poll", Ct);
            if (poll.StatusCode == HttpStatusCode.NoContent) continue;
            poll.EnsureSuccessStatusCode();
            var job = (await poll.Content.ReadFromJsonAsync<RemoteJob>(ApiSession.Json, Ct))!;
            var complete = await worker.PostAsJsonAsync($"{basePath}/jobs/{job.Id}/complete?machineId={job.MachineId}", result, Ct);
            Assert.Equal(HttpStatusCode.NoContent, complete.StatusCode);
            return job;
        }
        throw new TimeoutException("Nenhum job recebido.");
    }

    [Fact]
    public async Task Wake_through_the_gateway_reaches_the_worker_and_returns_its_result()
    {
        PostgresDatabase.SkipIfUnavailable();
        var session = await ApiSession.RegisterAsync(fixture.Api, ApiFactory.GatewayOwner);
        var machine = await session.CreateMachineAsync(ApiSession.Machine(method: "TailscaleGateway"));
        var gateway = Worker(ApiFactory.GatewayKey);

        var offline = await session.Http.PostAsync($"/api/machines/{machine.Id}/wake", null, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, offline.StatusCode);

        await gateway.GetAsync("/api/gateway/poll", Ct);
        Assert.True(Assert.Single(await session.GetAsync<List<MachineResponse>>("/api/machines")).GatewayOnline);
        var wake = session.Http.PostAsync($"/api/machines/{machine.Id}/wake", null, Ct);
        var job = await ServeOneJobAsync(gateway, "/api/gateway", new RemoteJobResult(true, "Enviado pelo gateway de teste."));

        Assert.Equal((RemoteActions.Wake, "020000000001", "192.168.1.255"), (job.Action, job.MacAddress, job.BroadcastAddress));
        var response = await wake;
        response.EnsureSuccessStatusCode();
        Assert.Contains("gateway de teste", await response.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task Agent_executes_power_actions_with_its_own_key_only()
    {
        PostgresDatabase.SkipIfUnavailable();
        var session = await ApiSession.RegisterAsync(fixture.Api);
        var machine = await session.CreateMachineAsync();
        var key = (await (await session.Http.PostAsync($"/api/machines/{machine.Id}/agent-key", null, Ct))
            .Content.ReadFromJsonAsync<AgentKey>(ApiSession.Json, Ct))!.Key;
        var agent = Worker(key);

        await agent.GetAsync($"/api/agent/{machine.Id}/poll", Ct);
        var action = session.Http.PostAsJsonAsync($"/api/machines/{machine.Id}/actions", new { action = "restart" }, Ct);
        var job = await ServeOneJobAsync(agent, $"/api/agent/{machine.Id}", new RemoteJobResult(true, "Simulação."));

        Assert.Equal(RemoteActions.Restart, job.Action);
        (await action).EnsureSuccessStatusCode();
        var otherMachine = await session.CreateMachineAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, (await agent.GetAsync($"/api/agent/{otherMachine.Id}/poll", Ct)).StatusCode);
    }

    [Fact]
    public async Task Revoked_agent_keys_stop_working()
    {
        PostgresDatabase.SkipIfUnavailable();
        var session = await ApiSession.RegisterAsync(fixture.Api);
        var machine = await session.CreateMachineAsync();
        var original = (await (await session.Http.PostAsync($"/api/machines/{machine.Id}/agent-key", null, Ct))
            .Content.ReadFromJsonAsync<AgentKey>(ApiSession.Json, Ct))!.Key;

        Assert.Equal(HttpStatusCode.NoContent, (await session.Http.DeleteAsync($"/api/machines/{machine.Id}/agent-key", Ct)).StatusCode);

        var replacement = (await (await session.Http.PostAsync($"/api/machines/{machine.Id}/agent-key", null, Ct))
            .Content.ReadFromJsonAsync<AgentKey>(ApiSession.Json, Ct))!.Key;
        Assert.NotEqual(original, replacement);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Worker(original).GetAsync($"/api/agent/{machine.Id}/poll", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Worker(replacement).GetAsync($"/api/agent/{machine.Id}/poll", Ct)).StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("wrong-key")]
    public async Task Gateway_endpoints_require_the_gateway_key(string? key)
    {
        PostgresDatabase.SkipIfUnavailable();
        var client = key is null ? fixture.Api.CreateClient() : Worker(key);

        var response = await client.GetAsync("/api/gateway/poll", Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
