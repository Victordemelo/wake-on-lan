using System.Net.Sockets;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using RemoteWake.Api.Contracts;
using RemoteWake.Api.Data;
using RemoteWake.Api.Models;
using RemoteWake.Api.Services;
using RemoteWake.Shared;

namespace RemoteWake.Api.Endpoints;

public static class MachineEndpoints
{
    public static void MapMachineEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/activity", ListActivityAsync).RequireAuthorization();
        app.MapGet("/api/status", GetStatusAsync).RequireAuthorization();

        var machines = app.MapGroup("/api/machines").RequireAuthorization();
        machines.MapGet("/", ListAsync);
        machines.MapPost("/", CreateAsync);
        machines.MapPut("/{id:guid}", UpdateAsync);
        machines.MapDelete("/{id:guid}", DeleteAsync);
        machines.MapPost("/{id:guid}/wake", WakeAsync).RequireRateLimiting(RateLimits.Commands);
        machines.MapPost("/{id:guid}/actions", RunActionAsync).RequireRateLimiting(RateLimits.Commands);
        machines.MapPost("/{id:guid}/agent-key", GetAgentKeyAsync);
        machines.MapDelete("/{id:guid}/agent-key", RevokeAgentKeyAsync);
    }

    private static async Task<IResult> ListActivityAsync(ClaimsPrincipal principal, AppDbContext database)
    {
        var owner = principal.UserId();
        // Current name while the machine exists; the recorded name after it is removed.
        return Results.Ok(await database.WakeAttempts.AsNoTracking()
            .Where(item => item.OwnerId == owner)
            .OrderByDescending(item => item.RequestedAt).Take(100)
            .Select(item => new ActivityResponse(item.Id, item.MachineId,
                item.Machine != null ? item.Machine.Name : item.MachineName,
                item.Action, item.Succeeded, item.Message, item.RequestedAt))
            .ToListAsync());
    }

    // Gateway state for this account: it only serves the account in GATEWAY_OWNER_EMAIL.
    private static async Task<IResult> GetStatusAsync(ClaimsPrincipal principal, AppDbContext database,
        RemoteJobBroker jobs, WorkerKeys keys)
    {
        var userId = principal.UserId();
        var email = await database.Users.Where(user => user.Id == userId).Select(user => user.Email).SingleAsync();
        var configured = keys.GatewayConfigured && keys.GatewayOwnerEmail == email;
        return Results.Ok(new StatusResponse(configured, configured && jobs.GatewayOnline));
    }

    private static async Task<IResult> ListAsync(ClaimsPrincipal principal, AppDbContext database,
        RemoteJobBroker jobs, WorkerKeys keys)
    {
        var userId = principal.UserId();
        var gatewayOnline = await GatewayOnlineForAsync(userId, database, jobs, keys);
        var machines = await database.Machines.AsNoTracking()
            .Where(machine => machine.OwnerId == userId)
            .OrderBy(machine => machine.Name)
            .ToListAsync();
        return Results.Ok(machines.Select(machine => ToResponse(machine, jobs.AgentOnline(machine.Id), gatewayOnline)));
    }

    private static async Task<IResult> CreateAsync(MachineRequest request, ClaimsPrincipal principal, AppDbContext database)
    {
        if (!MachineRules.TryValidate(request, out var normalizedMac, out var error))
            return Results.BadRequest(new { message = error });

        var machine = new Machine
        {
            Name = request.Name.Trim(),
            MacAddress = normalizedMac,
            Hostname = request.Hostname?.Trim(),
            BroadcastAddress = request.BroadcastAddress.Trim(),
            WolPort = request.WolPort,
            WakeMethod = request.WakeMethod,
            OwnerId = principal.UserId()
        };
        database.Machines.Add(machine);
        await database.SaveChangesAsync();
        return Results.Created($"/api/machines/{machine.Id}", ToResponse(machine, false, false));
    }

    private static async Task<IResult> UpdateAsync(Guid id, MachineRequest request, ClaimsPrincipal principal,
        AppDbContext database, RemoteJobBroker jobs, WorkerKeys keys)
    {
        var machine = await FindOwnedAsync(id, principal, database);
        if (machine is null) return Results.NotFound();
        if (!MachineRules.TryValidate(request, out var normalizedMac, out var error))
            return Results.BadRequest(new { message = error });

        machine.Name = request.Name.Trim();
        machine.MacAddress = normalizedMac;
        machine.Hostname = request.Hostname?.Trim();
        machine.BroadcastAddress = request.BroadcastAddress.Trim();
        machine.WolPort = request.WolPort;
        machine.WakeMethod = request.WakeMethod;
        await database.SaveChangesAsync();
        var gatewayOnline = await GatewayOnlineForAsync(machine.OwnerId, database, jobs, keys);
        return Results.Ok(ToResponse(machine, jobs.AgentOnline(machine.Id), gatewayOnline));
    }

    private static async Task<IResult> DeleteAsync(Guid id, ClaimsPrincipal principal, AppDbContext database)
    {
        var machine = await FindOwnedAsync(id, principal, database);
        if (machine is null) return Results.NotFound();
        // The history stays: the database sets MachineId to null and keeps MachineName.
        database.Machines.Remove(machine);
        await database.SaveChangesAsync();
        return Results.NoContent();
    }

    private static async Task<IResult> WakeAsync(Guid id, ClaimsPrincipal principal, HttpContext context,
        AppDbContext database, MagicPacketService magicPacket, RemoteJobBroker jobs, WorkerKeys keys,
        TimeProvider time, CancellationToken cancellationToken)
    {
        var machine = await FindOwnedAsync(id, principal, database);
        if (machine is null) return Results.NotFound();

        var requestedAt = time.GetUtcNow();
        var success = false;
        string message;

        if (machine.WakeMethod == WakeMethod.TailscaleGateway)
        {
            var ownerEmail = await database.Users.Where(user => user.Id == machine.OwnerId)
                .Select(user => user.Email).SingleAsync(cancellationToken);
            if (!keys.GatewayConfigured || keys.GatewayOwnerEmail != ownerEmail)
                message = "Configure a chave e o e-mail do proprietário do gateway na API.";
            else if (!jobs.GatewayOnline)
                message = "O gateway residencial está offline.";
            else
            {
                var result = await jobs.DispatchAsync(new RemoteJob(Guid.NewGuid(), machine.Id, RemoteActions.Wake,
                    machine.MacAddress, machine.BroadcastAddress, machine.WolPort), true, cancellationToken);
                success = result.Succeeded;
                message = result.Message;
            }
        }
        else
        {
            try
            {
                await magicPacket.SendAsync(machine.MacAddress, machine.BroadcastAddress, machine.WolPort, cancellationToken);
                success = true;
                message = "Magic Packet enviado.";
            }
            catch (Exception exception) when (exception is SocketException or ArgumentException)
            {
                message = $"Não foi possível enviar o Magic Packet: {exception.Message}";
            }
        }

        if (success) machine.LastWakeRequestedAt = requestedAt;
        database.WakeAttempts.Add(NewAttempt(machine, RemoteActions.Wake, success, message, requestedAt, context));
        await database.SaveChangesAsync(CancellationToken.None);
        return success
            ? Results.Ok(new WakeResponse(true, message, requestedAt))
            : Results.BadRequest(new WakeResponse(false, message, requestedAt));
    }

    private static async Task<IResult> RunActionAsync(Guid id, ActionRequest request, ClaimsPrincipal principal,
        HttpContext context, AppDbContext database, RemoteJobBroker jobs, TimeProvider time,
        ILogger<RemoteJobBroker> logger, CancellationToken cancellationToken)
    {
        var machine = await FindOwnedAsync(id, principal, database);
        if (machine is null) return Results.NotFound();
        if (!RemoteActions.IsPowerAction(request.Action))
            return Results.BadRequest(new { message = "Ação não permitida." });

        var requestedAt = time.GetUtcNow();
        var result = !jobs.AgentOnline(id)
            ? new RemoteJobResult(false, "O agente da máquina está offline.")
            : await jobs.DispatchAsync(new RemoteJob(Guid.NewGuid(), id, request.Action!), false, cancellationToken);
        database.WakeAttempts.Add(NewAttempt(machine, request.Action!, result.Succeeded, result.Message, requestedAt, context));
        await database.SaveChangesAsync(CancellationToken.None);
        logger.LogInformation("Action {Action} for machine {MachineId} requested by {UserId}: {Succeeded}",
            request.Action, id, principal.UserId(), result.Succeeded);
        return result.Succeeded
            ? Results.Ok(result)
            : Results.Json(new { message = result.Message }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    private static async Task<IResult> GetAgentKeyAsync(Guid id, ClaimsPrincipal principal, AppDbContext database, WorkerKeys keys)
    {
        var machine = await FindOwnedAsync(id, principal, database);
        if (machine is null) return Results.NotFound();
        var key = keys.AgentKey(id, machine.AgentKeyVersion);
        return key is null
            ? Results.Json(new { message = "Configure AGENT_MASTER_KEY na API." }, statusCode: StatusCodes.Status503ServiceUnavailable)
            : Results.Ok(new { machineId = id, key });
    }

    private static async Task<IResult> RevokeAgentKeyAsync(Guid id, ClaimsPrincipal principal, AppDbContext database, RemoteJobBroker jobs)
    {
        var machine = await FindOwnedAsync(id, principal, database);
        if (machine is null) return Results.NotFound();
        machine.AgentKeyVersion++;
        await database.SaveChangesAsync();
        jobs.ForgetAgent(id);
        return Results.NoContent();
    }

    private static WakeAttempt NewAttempt(Machine machine, string action, bool succeeded, string message,
        DateTimeOffset requestedAt, HttpContext context) => new()
    {
        MachineId = machine.Id,
        OwnerId = machine.OwnerId,
        MachineName = machine.Name,
        Action = action,
        Succeeded = succeeded,
        Message = message,
        RequestedAt = requestedAt,
        IpAddress = ClientInfo.IpAddress(context)
    };

    private static async Task<bool> GatewayOnlineForAsync(Guid userId, AppDbContext database, RemoteJobBroker jobs, WorkerKeys keys)
    {
        if (!keys.GatewayConfigured || !jobs.GatewayOnline) return false;
        var email = await database.Users.Where(user => user.Id == userId).Select(user => user.Email).SingleAsync();
        return keys.GatewayOwnerEmail == email;
    }

    private static Task<Machine?> FindOwnedAsync(Guid id, ClaimsPrincipal principal, AppDbContext database)
    {
        var userId = principal.UserId();
        return database.Machines.SingleOrDefaultAsync(machine => machine.Id == id && machine.OwnerId == userId);
    }

    private static MachineResponse ToResponse(Machine machine, bool agentOnline, bool gatewayOnline) => new(
        machine.Id, machine.Name, machine.MacAddress, machine.Hostname, machine.BroadcastAddress, machine.WolPort,
        machine.WakeMethod, machine.LastWakeRequestedAt, machine.CreatedAt, agentOnline, gatewayOnline);
}
