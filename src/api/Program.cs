using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using RemoteWake.Api.Data;
using RemoteWake.Api.Endpoints;
using RemoteWake.Api.Models;
using RemoteWake.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Database")));
builder.Services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(new RemoteJobBrokerOptions());
builder.Services.AddSingleton<RemoteJobBroker>();
builder.Services.AddSingleton<MagicPacketService>();
builder.Services.AddSingleton<WorkerKeys>();
builder.Services.AddSingleton<SetupCode>();
builder.Services.AddSingleton(new LoginSessionOptions
{
    AlwaysSecure = builder.Configuration.GetValue<bool>("Session:AlwaysSecure")
});
builder.Services.AddScoped<SessionService>();
builder.Services.AddScoped<SecurityLog>();
builder.Services.AddSingleton<AccountLocks>();
builder.Services.AddSingleton<FailedLoginRecorder>();
builder.Services.AddHostedService(services => services.GetRequiredService<FailedLoginRecorder>());

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = (context, cancellationToken) => new ValueTask(context.HttpContext.Response.WriteAsJsonAsync(
        new { message = "Muitas tentativas em pouco tempo. Aguarde um minuto e tente de novo." }, cancellationToken));
    // Login and registration: per client IP (the real one when behind a trusted proxy).
    options.AddPolicy(RateLimits.Auth, context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = builder.Configuration.GetValue("RateLimits:AuthPerMinute", 20),
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        }));
    // Wake and power commands: per account, so one user cannot flood the network.
    options.AddPolicy(RateLimits.Commands, context => RateLimitPartition.GetFixedWindowLimiter(
        context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = builder.Configuration.GetValue("RateLimits:CommandsPerMinute", 12),
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        }));
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
builder.Services
    .AddAuthentication(SessionAuthenticationHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, SessionAuthenticationHandler>(SessionAuthenticationHandler.SchemeName, null);
builder.Services.AddAuthorization();
// Sessions are opaque tokens stored in the database, so nothing is protected with
// Data Protection; in-memory keys avoid persisting unused keys inside the container.
builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await DatabaseMigrator.MigrateAsync(database, app.Logger);

    if (await AccountCommands.TryRunAsync(args, scope.ServiceProvider) is int exitCode)
    {
        Environment.ExitCode = exitCode;
        return;
    }

    if (!await database.Users.AnyAsync())
    {
        var setup = app.Services.GetRequiredService<SetupCode>();
        if (setup.IsConfigured)
            app.Logger.LogWarning("Nenhuma conta cadastrada. Use o código definido em SETUP_TOKEN para criar a primeira conta.");
        else
            app.Logger.LogWarning("Nenhuma conta cadastrada. Código de configuração para criar a primeira conta: {SetupCode}", setup.Value);
    }
}

app.UseForwardedHeaders();
app.UseCsrfGuard();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapHealthChecks("/health");
app.MapAuthEndpoints();
app.MapAccountEndpoints();
app.MapMachineEndpoints();
app.MapWorkerEndpoints();

app.Run();

public partial class Program;
