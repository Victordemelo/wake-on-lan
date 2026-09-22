using System.Collections.Concurrent;
using System.Threading.Channels;

namespace RemoteWake.Api.Services;

public sealed record RemoteJob(Guid Id, Guid MachineId, string Action, string? MacAddress = null,
    string? BroadcastAddress = null, int WolPort = 9)
{
    public DateTimeOffset ExpiresAt { get; init; } = DateTimeOffset.UtcNow.AddSeconds(20);
}
public sealed record RemoteJobResult(bool Succeeded, string Message);

// Jobs live only while the requesting HTTP call is active. A worker never receives
// an old power command after an API restart or after the request times out.
public sealed class RemoteJobBroker
{
    private sealed record Pending(RemoteJob Job, bool Gateway, TaskCompletionSource<RemoteJobResult> Completion);

    private readonly Channel<Guid> gatewayQueue = Channel.CreateUnbounded<Guid>();
    private readonly ConcurrentDictionary<Guid, Channel<Guid>> agentQueues = new();
    private readonly ConcurrentDictionary<Guid, Pending> pending = new();
    private readonly ConcurrentDictionary<Guid, DateTimeOffset> agentSeen = new();
    private readonly ConcurrentDictionary<string, byte> active = new();
    private long gatewaySeenTicks;

    public bool GatewayOnline => DateTimeOffset.UtcNow.UtcTicks - Interlocked.Read(ref gatewaySeenTicks)
        < TimeSpan.FromSeconds(35).Ticks;
    public bool AgentOnline(Guid machineId) => agentSeen.TryGetValue(machineId, out var seen)
        && DateTimeOffset.UtcNow - seen < TimeSpan.FromSeconds(35);

    public async Task<RemoteJobResult> DispatchAsync(RemoteJob job, bool gateway, CancellationToken cancellationToken)
    {
        var activeKey = $"{gateway}:{job.MachineId}";
        if (!active.TryAdd(activeKey, 0)) return new(false, "Já existe uma solicitação em andamento para esta máquina.");
        var completion = new TaskCompletionSource<RemoteJobResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        pending[job.Id] = new Pending(job, gateway, completion);
        var queue = gateway ? gatewayQueue : agentQueues.GetOrAdd(job.MachineId, _ => Channel.CreateUnbounded<Guid>());
        queue.Writer.TryWrite(job.Id);
        try
        {
            return await completion.Task.WaitAsync(TimeSpan.FromSeconds(25), cancellationToken);
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
        if (gateway) Interlocked.Exchange(ref gatewaySeenTicks, DateTimeOffset.UtcNow.UtcTicks);
        else agentSeen[machineId] = DateTimeOffset.UtcNow;

        var queue = gateway ? gatewayQueue : agentQueues.GetOrAdd(machineId, _ => Channel.CreateUnbounded<Guid>());
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        try
        {
            while (true)
            {
                var id = await queue.Reader.ReadAsync(timeout.Token);
                if (pending.TryGetValue(id, out var current) && current.Job.ExpiresAt > DateTimeOffset.UtcNow) return current.Job;
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
