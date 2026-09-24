using Microsoft.EntityFrameworkCore;
using RemoteWake.Api.Data;
using RemoteWake.Api.Services;
using RemoteWake.Shared;

namespace RemoteWake.Api.Endpoints;

// Long-polling endpoints used by the gateway and the agents. They authenticate
// with the X-Remote-Wake-Key header, never with the browser session.
public static class WorkerEndpoints
{
    private const string KeyHeader = "X-Remote-Wake-Key";

    public static void MapWorkerEndpoints(this IEndpointRouteBuilder app)
    {
        var gateway = app.MapGroup("/api/gateway");
        gateway.MapGet("/poll", async (HttpRequest request, WorkerKeys keys, RemoteJobBroker jobs,
            CancellationToken cancellationToken) =>
        {
            if (!keys.IsGateway(request.Headers[KeyHeader])) return Results.Unauthorized();
            var job = await jobs.PollAsync(true, Guid.Empty, cancellationToken);
            return job is null ? Results.NoContent() : Results.Ok(job);
        });
        gateway.MapPost("/jobs/{id:guid}/complete", (Guid id, RemoteJobResult result, Guid machineId,
            HttpRequest request, WorkerKeys keys, RemoteJobBroker jobs) =>
        {
            if (!keys.IsGateway(request.Headers[KeyHeader])) return Results.Unauthorized();
            return jobs.Complete(id, machineId, true, result) ? Results.NoContent() : Results.NotFound();
        });

        var agent = app.MapGroup("/api/agent/{machineId:guid}");
        agent.MapGet("/poll", async (Guid machineId, HttpRequest request, WorkerKeys keys, AppDbContext database,
            RemoteJobBroker jobs, TimeProvider time, CancellationToken cancellationToken) =>
        {
            var version = await AgentKeyVersionAsync(machineId, database, cancellationToken);
            if (version is null || !keys.IsAgent(machineId, version.Value, request.Headers[KeyHeader])) return Results.Unauthorized();
            if (!jobs.AgentOnline(machineId))
                await BootConfirmation.RecordIfRecentWakeAsync(machineId, database, jobs, time, cancellationToken);
            var job = await jobs.PollAsync(false, machineId, cancellationToken);
            // The key may have been revoked (or the machine removed) while the poll was waiting.
            if (await AgentKeyVersionAsync(machineId, database, cancellationToken) != version) return Results.Unauthorized();
            return job is null ? Results.NoContent() : Results.Ok(job);
        });
        agent.MapPost("/jobs/{id:guid}/complete", async (Guid machineId, Guid id, RemoteJobResult result,
            HttpRequest request, WorkerKeys keys, AppDbContext database, RemoteJobBroker jobs, CancellationToken cancellationToken) =>
        {
            var version = await AgentKeyVersionAsync(machineId, database, cancellationToken);
            if (version is null || !keys.IsAgent(machineId, version.Value, request.Headers[KeyHeader])) return Results.Unauthorized();
            return jobs.Complete(id, machineId, false, result) ? Results.NoContent() : Results.NotFound();
        });
    }

    private static Task<int?> AgentKeyVersionAsync(Guid machineId, AppDbContext database, CancellationToken cancellationToken) =>
        database.Machines.AsNoTracking().Where(machine => machine.Id == machineId)
            .Select(machine => (int?)machine.AgentKeyVersion).SingleOrDefaultAsync(cancellationToken);
}
