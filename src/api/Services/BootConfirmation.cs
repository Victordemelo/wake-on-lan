using Microsoft.EntityFrameworkCore;
using RemoteWake.Api.Data;
using RemoteWake.Api.Models;
using RemoteWake.Shared;

namespace RemoteWake.Api.Services;

// "Ligar" only proves that the Magic Packet left. When the machine's agent connects
// shortly after a successful wake request, the machine really started: record that.
public static class BootConfirmation
{
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(10);

    // Call when an agent polls while the API considered it offline.
    public static async Task RecordIfRecentWakeAsync(Guid machineId, AppDbContext database, RemoteJobBroker jobs,
        TimeProvider time, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var machine = await database.Machines.AsNoTracking().SingleOrDefaultAsync(item => item.Id == machineId, cancellationToken);
        if (machine?.LastWakeRequestedAt is not DateTimeOffset requested) return;
        // Online state lives in memory, so only wakes requested since this API started are observable.
        if (requested < jobs.StartedAt || now - requested > Window) return;
        if (await database.WakeAttempts.AnyAsync(item => item.MachineId == machineId
            && item.Action == RemoteActions.Online && item.RequestedAt >= requested, cancellationToken)) return;

        var seconds = Math.Max(1, (int)Math.Round((now - requested).TotalSeconds));
        database.WakeAttempts.Add(new WakeAttempt
        {
            MachineId = machine.Id,
            OwnerId = machine.OwnerId,
            MachineName = machine.Name,
            Action = RemoteActions.Online,
            Succeeded = true,
            Message = $"A máquina ligou: o agente conectou {seconds} s após o pedido.",
            RequestedAt = now
        });
        await database.SaveChangesAsync(cancellationToken);
    }
}
