using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Sockets;
using System.Security.Claims;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
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
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();

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

var origins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>()
    ?? ["http://localhost:3000", "http://localhost:5173"];
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
    policy.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await database.Database.EnsureCreatedAsync();
}

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapHealthChecks("/health");

var auth = app.MapGroup("/api/auth");

auth.MapPost("/register", async (
    RegisterRequest request,
    AppDbContext database,
    IPasswordHasher<User> passwordHasher,
    TokenService tokens) =>
{
    var name = request.Name.Trim();
    var email = request.Email.Trim().ToLowerInvariant();

    if (name.Length < 2 || !email.Contains('@') || request.Password.Length < 8)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["credentials"] = ["Informe nome, e-mail válido e senha com pelo menos 8 caracteres."]
        });
    }

    if (await database.Users.AnyAsync(user => user.Email == email))
    {
        return Results.Conflict(new { message = "Este e-mail já está cadastrado." });
    }

    var user = new User { Name = name, Email = email, PasswordHash = string.Empty };
    user.PasswordHash = passwordHasher.HashPassword(user, request.Password);
    database.Users.Add(user);
    await database.SaveChangesAsync();

    return Results.Ok(new AuthResponse(
        tokens.Create(user),
        new UserResponse(user.Id, user.Name, user.Email)));
});

auth.MapPost("/login", async (
    LoginRequest request,
    AppDbContext database,
    IPasswordHasher<User> passwordHasher,
    TokenService tokens) =>
{
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

machines.MapGet("/", async (ClaimsPrincipal principal, AppDbContext database) =>
{
    var userId = GetUserId(principal);
    var result = await database.Machines
        .Where(machine => machine.OwnerId == userId)
        .OrderBy(machine => machine.Name)
        .Select(machine => new MachineResponse(
            machine.Id,
            machine.Name,
            machine.MacAddress,
            machine.Hostname,
            machine.BroadcastAddress,
            machine.WolPort,
            machine.WakeMethod,
            machine.LastWakeRequestedAt,
            machine.CreatedAt))
        .ToListAsync();
    return Results.Ok(result);
});

machines.MapPost("/", async (
    MachineRequest request,
    ClaimsPrincipal principal,
    AppDbContext database) =>
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
    return Results.Created($"/api/machines/{machine.Id}", ToResponse(machine));
});

machines.MapPut("/{id:guid}", async (
    Guid id,
    MachineRequest request,
    ClaimsPrincipal principal,
    AppDbContext database) =>
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
    return Results.Ok(ToResponse(machine));
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
    CancellationToken cancellationToken) =>
{
    var machine = await FindOwnedMachine(id, principal, database);
    if (machine is null) return Results.NotFound();

    var requestedAt = DateTimeOffset.UtcNow;
    var success = false;
    string message;

    if (machine.WakeMethod == WakeMethod.TailscaleGateway)
    {
        message = "O gateway Tailscale ainda não foi pareado. Esse modo será habilitado na próxima fase.";
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
    await database.SaveChangesAsync(cancellationToken);
    return success
        ? Results.Ok(new WakeResponse(true, message, requestedAt))
        : Results.BadRequest(new WakeResponse(false, message, requestedAt));
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
    if (request.Name.Trim().Length < 2)
    {
        normalizedMac = string.Empty;
        error = "Informe um nome com pelo menos 2 caracteres.";
        return false;
    }

    if (!MagicPacketService.TryNormalizeMac(request.MacAddress, out normalizedMac))
    {
        error = "Informe um endereço MAC válido.";
        return false;
    }

    if (request.WolPort is < 1 or > 65535)
    {
        error = "A porta deve estar entre 1 e 65535.";
        return false;
    }

    if (string.IsNullOrWhiteSpace(request.BroadcastAddress))
    {
        error = "Informe o endereço de broadcast, IP público ou DDNS.";
        return false;
    }

    error = string.Empty;
    return true;
}

static MachineResponse ToResponse(Machine machine) => new(
    machine.Id,
    machine.Name,
    machine.MacAddress,
    machine.Hostname,
    machine.BroadcastAddress,
    machine.WolPort,
    machine.WakeMethod,
    machine.LastWakeRequestedAt,
    machine.CreatedAt);

public partial class Program;
