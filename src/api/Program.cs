using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Sockets;
using System.Security.Claims;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using RemoteWake.Api.Contracts;
using RemoteWake.Api.Data;
using RemoteWake.Api.Models;
using RemoteWake.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Database")));
builder.Services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();
builder.Services.AddSingleton<TokenService>();
builder.Services.AddSingleton<MagicPacketService>();
builder.Services.AddSingleton<RemoteJobBroker>();
builder.Services.AddSingleton<WorkerKeys>();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = 429;
    options.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks().AddDbContextCheck<AppDbContext>();
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    // Only proxies inside these networks may report the client IP and scheme.
    // Keep the API reachable exclusively through them (see compose.yaml).
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    foreach (var network in builder.Configuration.GetSection("ForwardedHeaders:TrustedNetworks").Get<string[]>() ?? [])
        options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
});

var jwtKey = builder.Configuration["Jwt:Key"]
    ?? throw new InvalidOperationException("Configure Jwt:Key.");
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidateAudience = true,
            ValidAudience = builder.Configuration["Jwt:Audience"],
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1)
        };
    });
builder.Services.AddAuthorization();

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await database.Database.EnsureCreatedAsync();
    await SchemaUpgrades.ApplyAsync(database);
}

app.UseForwardedHeaders();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapHealthChecks("/health");

var auth = app.MapGroup("/api/auth").RequireRateLimiting("auth");
auth.MapGet("/registration", async (AppDbContext database, IConfiguration configuration) =>
    Results.Ok(new { open = configuration.GetValue<bool>("Registration:Open") || !await database.Users.AnyAsync() }))
    .DisableRateLimiting();
var registrationGate = new SemaphoreSlim(1, 1);

auth.MapPost("/register", async (
    RegisterRequest request,
    AppDbContext database,
    IPasswordHasher<User> passwordHasher,
    TokenService tokens,
    IConfiguration configuration) =>
{
    var name = request.Name?.Trim() ?? "";
    var email = request.Email?.Trim().ToLowerInvariant() ?? "";

    if (name.Length is < 2 or > 100 || email.Length > 254 || !email.Contains('@') || request.Password is null || request.Password.Length is < 8 or > 256)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["credentials"] = ["Informe nome, e-mail válido e senha com pelo menos 8 caracteres."]
        });
    }

    await registrationGate.WaitAsync();
    try
    {
        if (await database.Users.AnyAsync(user => user.Email == email))
            return Results.Conflict(new { message = "Este e-mail já está cadastrado." });
        if (!configuration.GetValue<bool>("Registration:Open")
            && await database.Users.AnyAsync())
            return Results.Problem("O cadastro está fechado. O administrador pode habilitá-lo temporariamente.", statusCode: 403);

        var user = new User { Name = name, Email = email, PasswordHash = string.Empty };
        user.PasswordHash = passwordHasher.HashPassword(user, request.Password);
        database.Users.Add(user);
        await database.SaveChangesAsync();

        return Results.Ok(new AuthResponse(
            tokens.Create(user),
            new UserResponse(user.Id, user.Name, user.Email)));
    }
    finally
    {
        registrationGate.Release();
    }
});

auth.MapPost("/login", async (
    LoginRequest request,
    AppDbContext database,
    IPasswordHasher<User> passwordHasher,
    TokenService tokens) =>
{
    if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrEmpty(request.Password)) return Results.Unauthorized();
    var email = request.Email.Trim().ToLowerInvariant();
    var user = await database.Users.SingleOrDefaultAsync(item => item.Email == email);

    if (user is null || passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password)
        == PasswordVerificationResult.Failed)
    {
        return Results.Unauthorized();
    }

    return Results.Ok(new AuthResponse(
        tokens.Create(user),
        new UserResponse(user.Id, user.Name, user.Email)));
});

var machines = app.MapGroup("/api/machines").RequireAuthorization();
app.MapGet("/api/activity", async (ClaimsPrincipal principal, AppDbContext database) =>
{
    var owner = GetUserId(principal);
    return Results.Ok(await database.WakeAttempts.AsNoTracking()
        .Where(item => item.Machine!.OwnerId == owner)
        .OrderByDescending(item => item.RequestedAt).Take(100)
        .Select(item => new { item.Id, item.MachineId, machineName = item.Machine!.Name,
            item.Action, item.Succeeded, item.Message, item.RequestedAt }).ToListAsync());
}).RequireAuthorization();

machines.MapGet("/", async (ClaimsPrincipal principal, AppDbContext database,
    RemoteJobBroker jobs, WorkerKeys keys) =>
{
    var userId = GetUserId(principal);
    var ownerEmail = await database.Users.Where(user => user.Id == userId)
        .Select(user => user.Email).SingleAsync();
    var gatewayOnline = keys.GatewayConfigured && keys.GatewayOwnerEmail == ownerEmail && jobs.GatewayOnline;
    var result = (await database.Machines
        .Where(machine => machine.OwnerId == userId)
        .OrderBy(machine => machine.Name)
        .ToListAsync())
        .Select(machine => ToResponse(machine, jobs.AgentOnline(machine.Id), gatewayOnline))
        .ToList();
    return Results.Ok(result);
});

machines.MapPost("/", async (
    MachineRequest request,
    ClaimsPrincipal principal,
    AppDbContext database,
    RemoteJobBroker jobs) =>
{
    if (!ValidateMachine(request, out var normalizedMac, out var error))
    {
        return Results.BadRequest(new { message = error });
    }

    var machine = new Machine
    {
        Name = request.Name.Trim(),
        MacAddress = normalizedMac,
        Hostname = request.Hostname?.Trim(),
        BroadcastAddress = request.BroadcastAddress.Trim(),
        WolPort = request.WolPort,
        WakeMethod = request.WakeMethod,
        OwnerId = GetUserId(principal)
    };
    database.Machines.Add(machine);
    await database.SaveChangesAsync();
    return Results.Created($"/api/machines/{machine.Id}", ToResponse(machine, false, false));
});

machines.MapPut("/{id:guid}", async (
    Guid id,
    MachineRequest request,
    ClaimsPrincipal principal,
    AppDbContext database,
    RemoteJobBroker jobs) =>
{
    var machine = await FindOwnedMachine(id, principal, database);
    if (machine is null) return Results.NotFound();
    if (!ValidateMachine(request, out var normalizedMac, out var error))
        return Results.BadRequest(new { message = error });

    machine.Name = request.Name.Trim();
    machine.MacAddress = normalizedMac;
    machine.Hostname = request.Hostname?.Trim();
    machine.BroadcastAddress = request.BroadcastAddress.Trim();
    machine.WolPort = request.WolPort;
    machine.WakeMethod = request.WakeMethod;
    await database.SaveChangesAsync();
    return Results.Ok(ToResponse(machine, jobs.AgentOnline(machine.Id), false));
});

machines.MapDelete("/{id:guid}", async (
    Guid id,
    ClaimsPrincipal principal,
    AppDbContext database) =>
{
    var machine = await FindOwnedMachine(id, principal, database);
    if (machine is null) return Results.NotFound();
    database.Machines.Remove(machine);
    await database.SaveChangesAsync();
    return Results.NoContent();
});

machines.MapPost("/{id:guid}/wake", async (
    Guid id,
    ClaimsPrincipal principal,
    AppDbContext database,
    MagicPacketService magicPacket,
    RemoteJobBroker jobs,
    WorkerKeys keys,
    CancellationToken cancellationToken) =>
{
    var machine = await FindOwnedMachine(id, principal, database);
    if (machine is null) return Results.NotFound();

    var requestedAt = DateTimeOffset.UtcNow;
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
            var result = await jobs.DispatchAsync(new RemoteJob(Guid.NewGuid(), machine.Id, "wake",
                machine.MacAddress, machine.BroadcastAddress, machine.WolPort), true, cancellationToken);
            success = result.Succeeded;
            message = result.Message;
            if (success) machine.LastWakeRequestedAt = requestedAt;
        }
    }
    else try
    {
        await magicPacket.SendAsync(
            machine.MacAddress,
            machine.BroadcastAddress,
            machine.WolPort,
            cancellationToken);
        machine.LastWakeRequestedAt = requestedAt;
        success = true;
        message = "Magic Packet enviado.";
    }
    catch (Exception exception) when (exception is SocketException or ArgumentException)
    {
        message = $"Não foi possível enviar o Magic Packet: {exception.Message}";
    }

    database.WakeAttempts.Add(new WakeAttempt
    {
        MachineId = machine.Id,
        Succeeded = success,
        Message = message,
        RequestedAt = requestedAt
    });
    await database.SaveChangesAsync(CancellationToken.None);
    return success
        ? Results.Ok(new WakeResponse(true, message, requestedAt))
        : Results.BadRequest(new WakeResponse(false, message, requestedAt));
});

machines.MapPost("/{id:guid}/agent-key", async (Guid id, ClaimsPrincipal principal,
    AppDbContext database, WorkerKeys keys) =>
{
    var machine = await FindOwnedMachine(id, principal, database);
    if (machine is null) return Results.NotFound();
    var key = keys.AgentKey(id, machine.AgentKeyVersion);
    return key is null ? Results.Problem("Configure Agent:MasterKey na API.", statusCode: 503)
        : Results.Ok(new { machineId = id, key });
});

machines.MapDelete("/{id:guid}/agent-key", async (Guid id, ClaimsPrincipal principal,
    AppDbContext database, RemoteJobBroker jobs) =>
{
    var machine = await FindOwnedMachine(id, principal, database);
    if (machine is null) return Results.NotFound();
    machine.AgentKeyVersion++;
    await database.SaveChangesAsync();
    jobs.ForgetAgent(id);
    return Results.NoContent();
});

machines.MapPost("/{id:guid}/actions", async (Guid id, ActionRequest request,
    ClaimsPrincipal principal, AppDbContext database, RemoteJobBroker jobs,
    ILogger<Program> logger, CancellationToken cancellationToken) =>
{
    if (await FindOwnedMachine(id, principal, database) is null) return Results.NotFound();
    if (request.Action is not ("shutdown" or "restart"))
        return Results.BadRequest(new { message = "Ação não permitida." });
    var result = !jobs.AgentOnline(id)
        ? new RemoteJobResult(false, "O agente da máquina está offline.")
        : await jobs.DispatchAsync(new RemoteJob(Guid.NewGuid(), id, request.Action), false, cancellationToken);
    database.WakeAttempts.Add(new WakeAttempt { MachineId = id, Action = request.Action,
        Succeeded = result.Succeeded, Message = result.Message });
    await database.SaveChangesAsync(CancellationToken.None);
    logger.LogInformation("Action {Action} for machine {MachineId} requested by {UserId}: {Succeeded}",
        request.Action, id, GetUserId(principal), result.Succeeded);
    return result.Succeeded ? Results.Ok(result) : Results.Problem(result.Message, statusCode: 503);
});

var gateway = app.MapGroup("/api/gateway");
gateway.MapGet("/poll", async (HttpRequest request, WorkerKeys keys,
    RemoteJobBroker jobs, CancellationToken cancellationToken) =>
{
    if (!keys.IsGateway(request.Headers["X-Remote-Wake-Key"])) return Results.Unauthorized();
    var job = await jobs.PollAsync(true, Guid.Empty, cancellationToken);
    return job is null ? Results.NoContent() : Results.Ok(job);
});
gateway.MapPost("/jobs/{id:guid}/complete", (Guid id, RemoteJobResult result,
    Guid machineId, HttpRequest request, WorkerKeys keys, RemoteJobBroker jobs) =>
{
    if (!keys.IsGateway(request.Headers["X-Remote-Wake-Key"])) return Results.Unauthorized();
    return jobs.Complete(id, machineId, true, result) ? Results.NoContent() : Results.NotFound();
});

var agent = app.MapGroup("/api/agent/{machineId:guid}");
agent.MapGet("/poll", async (Guid machineId, HttpRequest request, WorkerKeys keys,
    AppDbContext database, RemoteJobBroker jobs, CancellationToken cancellationToken) =>
{
    var machine = await database.Machines.AsNoTracking().SingleOrDefaultAsync(item => item.Id == machineId, cancellationToken);
    if (machine is null || !keys.IsAgent(machineId, machine.AgentKeyVersion, request.Headers["X-Remote-Wake-Key"])) return Results.Unauthorized();
    var job = await jobs.PollAsync(false, machineId, cancellationToken);
    var version = await database.Machines.AsNoTracking().Where(item => item.Id == machineId)
        .Select(item => (int?)item.AgentKeyVersion).SingleOrDefaultAsync(cancellationToken);
    if (version != machine.AgentKeyVersion) return Results.Unauthorized();
    return job is null ? Results.NoContent() : Results.Ok(job);
});
agent.MapPost("/jobs/{id:guid}/complete", async (Guid machineId, Guid id,
    RemoteJobResult result, HttpRequest request, WorkerKeys keys, AppDbContext database,
    RemoteJobBroker jobs, CancellationToken cancellationToken) =>
{
    var machine = await database.Machines.AsNoTracking().SingleOrDefaultAsync(item => item.Id == machineId, cancellationToken);
    if (machine is null || !keys.IsAgent(machineId, machine.AgentKeyVersion, request.Headers["X-Remote-Wake-Key"])) return Results.Unauthorized();
    return jobs.Complete(id, machineId, false, result) ? Results.NoContent() : Results.NotFound();
});

app.Run();

static Guid GetUserId(ClaimsPrincipal principal)
{
    var value = principal.FindFirstValue(JwtRegisteredClaimNames.Sub)
        ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);
    return Guid.Parse(value!);
}

static Task<Machine?> FindOwnedMachine(Guid id, ClaimsPrincipal principal, AppDbContext database)
{
    var userId = GetUserId(principal);
    return database.Machines.SingleOrDefaultAsync(machine => machine.Id == id && machine.OwnerId == userId);
}

static bool ValidateMachine(MachineRequest request, out string normalizedMac, out string error)
{
    if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length is < 2 or > 100)
    {
        normalizedMac = string.Empty;
        error = "Informe um nome com pelo menos 2 caracteres.";
        return false;
    }

    if (!MagicPacketService.TryNormalizeMac(request.MacAddress ?? "", out normalizedMac))
    {
        error = "Informe um endereço MAC válido.";
        return false;
    }

    if (request.WolPort is < 1 or > 65535)
    {
        error = "A porta deve estar entre 1 e 65535.";
        return false;
    }

    if (!Enum.IsDefined(request.WakeMethod) || string.IsNullOrWhiteSpace(request.BroadcastAddress) || request.BroadcastAddress.Length > 253)
    {
        error = "Informe o endereço de broadcast, IP público ou DDNS.";
        return false;
    }

    error = string.Empty;
    return true;
}

static MachineResponse ToResponse(Machine machine, bool agentOnline, bool gatewayOnline) => new(
    machine.Id,
    machine.Name,
    machine.MacAddress,
    machine.Hostname,
    machine.BroadcastAddress,
    machine.WolPort,
    machine.WakeMethod,
    machine.LastWakeRequestedAt,
    machine.CreatedAt,
    agentOnline,
    gatewayOnline);

public partial class Program;
