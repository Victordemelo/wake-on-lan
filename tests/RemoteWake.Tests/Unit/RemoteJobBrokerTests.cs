using Microsoft.Extensions.Time.Testing;
using RemoteWake.Api.Services;
using RemoteWake.Shared;

namespace RemoteWake.Tests.Unit;

public sealed class RemoteJobBrokerTests
{
    private readonly FakeTimeProvider time = new(new DateTimeOffset(2026, 9, 23, 12, 0, 0, TimeSpan.Zero));
    private readonly RemoteJobBroker broker;
    private readonly Guid machine = Guid.NewGuid();
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public RemoteJobBrokerTests() => broker = new RemoteJobBroker(time, new RemoteJobBrokerOptions());

    private RemoteJob Restart() => new(Guid.NewGuid(), machine, RemoteActions.Restart);

    [Fact]
    public async Task Agent_receives_the_job_and_its_result_reaches_the_requester()
    {
        var dispatch = broker.DispatchAsync(Restart(), gateway: false, Token);

        var job = await broker.PollAsync(gateway: false, machine, Token);

        Assert.NotNull(job);
        Assert.Equal(time.GetUtcNow().AddSeconds(20), job.ExpiresAt);
        Assert.True(broker.Complete(job.Id, machine, gateway: false, new RemoteJobResult(true, "ok")));
        Assert.Equal(new RemoteJobResult(true, "ok"), await dispatch);
    }

    [Fact]
    public async Task Poll_returns_nothing_when_no_job_arrives_in_time()
    {
        var poll = broker.PollAsync(gateway: true, Guid.Empty, Token);
        Assert.False(poll.IsCompleted);

        time.Advance(TimeSpan.FromSeconds(20));

        Assert.Null(await poll);
    }

    [Fact]
    public async Task Expired_jobs_are_never_delivered()
    {
        var dispatch = broker.DispatchAsync(Restart(), gateway: false, Token);
        time.Advance(TimeSpan.FromSeconds(21));

        var poll = broker.PollAsync(gateway: false, machine, Token);
        Assert.False(poll.IsCompleted);
        time.Advance(TimeSpan.FromSeconds(20));

        Assert.Null(await poll);
        var result = await dispatch;
        Assert.False(result.Succeeded);
        Assert.StartsWith("Sem confirmação", result.Message);
    }

    [Fact]
    public async Task Only_one_request_per_machine_runs_at_a_time()
    {
        var first = broker.DispatchAsync(Restart(), gateway: false, Token);

        var second = await broker.DispatchAsync(Restart(), gateway: false, Token);

        Assert.False(second.Succeeded);
        Assert.StartsWith("Já existe uma solicitação", second.Message);
        var job = await broker.PollAsync(gateway: false, machine, Token);
        broker.Complete(job!.Id, machine, gateway: false, new RemoteJobResult(true, "ok"));
        Assert.True((await first).Succeeded);
    }

    [Fact]
    public async Task Results_from_another_machine_or_worker_type_are_ignored()
    {
        var dispatch = broker.DispatchAsync(Restart(), gateway: false, Token);
        var job = await broker.PollAsync(gateway: false, machine, Token);

        Assert.False(broker.Complete(job!.Id, Guid.NewGuid(), gateway: false, new RemoteJobResult(true, "forged")));
        Assert.False(broker.Complete(job.Id, machine, gateway: true, new RemoteJobResult(true, "forged")));
        Assert.False(dispatch.IsCompleted);

        broker.Complete(job.Id, machine, gateway: false, new RemoteJobResult(false, "real"));
        Assert.Equal("real", (await dispatch).Message);
    }

    [Fact]
    public void Workers_are_online_for_35_seconds_after_their_last_poll()
    {
        Assert.False(broker.AgentOnline(machine));
        Assert.False(broker.GatewayOnline);

        _ = broker.PollAsync(gateway: false, machine, Token);
        _ = broker.PollAsync(gateway: true, Guid.Empty, Token);
        time.Advance(TimeSpan.FromSeconds(34));
        Assert.True(broker.AgentOnline(machine));
        Assert.True(broker.GatewayOnline);

        time.Advance(TimeSpan.FromSeconds(2));
        Assert.False(broker.AgentOnline(machine));
        Assert.False(broker.GatewayOnline);
    }

    [Fact]
    public async Task Revoking_an_agent_fails_its_pending_request()
    {
        _ = broker.PollAsync(gateway: false, machine, Token);
        var dispatch = broker.DispatchAsync(Restart(), gateway: false, Token);

        broker.ForgetAgent(machine);

        Assert.Equal(new RemoteJobResult(false, "Chave do agente revogada."), await dispatch);
        Assert.False(broker.AgentOnline(machine));
    }

    [Fact]
    public async Task Requester_cancellation_is_reported_without_waiting_for_the_worker()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var dispatch = broker.DispatchAsync(Restart(), gateway: false, cancellation.Token);

        await cancellation.CancelAsync();

        var result = await dispatch;
        Assert.False(result.Succeeded);
        Assert.StartsWith("Solicitação interrompida", result.Message);
    }
}
