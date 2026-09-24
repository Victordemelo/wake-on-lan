using System.Collections.Concurrent;
using System.Threading.Channels;
using RemoteWake.Shared;

namespace RemoteWake.Api.Services;

public sealed record RemoteJobBrokerOptions
{
    // A worker never executes a job older than this, even if it arrives late.
    public TimeSpan JobLifetime { get; init; } = TimeSpan.FromSeconds(20);
    // How long a long-poll request waits for a job before returning 204.
    public TimeSpan PollTimeout { get; init; } = TimeSpan.FromSeconds(20);
    // How long the requesting user waits for the worker's confirmation.
    public TimeSpan ResultTimeout { get; init; } = TimeSpan.FromSeconds(25);
    // A worker counts as online if it polled within this window.
    public TimeSpan OnlineWindow { get; init; } = TimeSpan.FromSeconds(35);
}

// Jobs live only while the requesting HTTP call is active. A worker never receives
// an old power command after an API restart or after the request times out.
public sealed class RemoteJobBroker(TimeProvider time, RemoteJobBrokerOptions options)
{
    private sealed record Pending(RemoteJob Job, bool Gateway, TaskCompletionSource<RemoteJobResult> Completion);

    private readonly Channel<Guid> gatewayQueue = Channel.CreateUnbounded<Guid>();
    private readonly ConcurrentDictionary<Guid, Channel<Guid>> agentQueues = new();
    private readonly ConcurrentDictionary<Guid, Pending> pending = new();
    private readonly ConcurrentDictionary<Guid, DateTimeOffset> agentSeen = new();
    private readonly ConcurrentDictionary<string, byte> active = new();
    private long gatewaySeenTicks;

    public DateTimeOffset StartedAt { get; } = time.GetUtcNow();

    public bool GatewayOnline => time.GetUtcNow().UtcTicks - Interlocked.Read(ref gatewaySeenTicks)
        < options.OnlineWindow.Ticks;
    public bool AgentOnline(Guid machineId) => agentSeen.TryGetValue(machineId, out var seen)
        && time.GetUtcNow() - seen < options.OnlineWindow;

    public async Task<RemoteJobResult> DispatchAsync(RemoteJob job, bool gateway, CancellationToken cancellationToken)
    {
        var activeKey = $"{gateway}:{job.MachineId}";
        if (!active.TryAdd(activeKey, 0)) return new(false, "Já existe uma solicitação em andamento para esta máquina.");
        job = job with { ExpiresAt = time.GetUtcNow() + options.JobLifetime };
        var completion = new TaskCompletionSource<RemoteJobResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        pending[job.Id] = new Pending(job, gateway, completion);
        var queue = gateway ? gatewayQueue : agentQueues.GetOrAdd(job.MachineId, _ => Channel.CreateUnbounded<Guid>());
        queue.Writer.TryWrite(job.Id);
        try
        {
            return await completion.Task.WaitAsync(options.ResultTimeout, time, cancellationToken);
        }
        catch (TimeoutException)
        {
            return new RemoteJobResult(false, "Sem confirmação do serviço local. A ação pode ter sido recebida; verifique a máquina antes de repetir.");
        }
        catch (OperationCanceledException)
        {
            return new(false, "Solicitação interrompida; confirme o estado da máquina antes de repetir.");
        }
        finally
        {
            pending.TryRemove(job.Id, out _);
            active.TryRemove(activeKey, out _);
        }
    }

    public async Task<RemoteJob?> PollAsync(bool gateway, Guid machineId, CancellationToken cancellationToken)
    {
        if (gateway) Interlocked.Exchange(ref gatewaySeenTicks, time.GetUtcNow().UtcTicks);
        else agentSeen[machineId] = time.GetUtcNow();

        var queue = gateway ? gatewayQueue : agentQueues.GetOrAdd(machineId, _ => Channel.CreateUnbounded<Guid>());
        using var timeout = new CancellationTokenSource(options.PollTimeout, time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            while (true)
            {
                var id = await queue.Reader.ReadAsync(linked.Token);
                if (pending.TryGetValue(id, out var current) && current.Job.ExpiresAt > time.GetUtcNow()) return current.Job;
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    public bool Complete(Guid id, Guid machineId, bool gateway, RemoteJobResult result)
    {
        return pending.TryGetValue(id, out var current)
            && current.Job.MachineId == machineId
            && current.Gateway == gateway
            && current.Completion.TrySetResult(result);
    }

    public void ForgetAgent(Guid machineId)
    {
        agentSeen.TryRemove(machineId, out _);
        foreach (var item in pending.Values.Where(item => !item.Gateway && item.Job.MachineId == machineId))
            item.Completion.TrySetResult(new(false, "Chave do agente revogada."));
    }
}
