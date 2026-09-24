using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using RemoteWake.Api.Contracts;
using RemoteWake.Api.Data;
using RemoteWake.Api.Models;
using RemoteWake.Api.Services;

namespace RemoteWake.Api.Endpoints;

public static class AuthEndpoints
{
    private static readonly SemaphoreSlim RegistrationGate = new(1, 1);
    // Verifying against a dummy hash keeps unknown e-mails as slow as wrong passwords.
    private static readonly User TimingUser = new() { Name = string.Empty, Email = string.Empty, PasswordHash = string.Empty };
    private static readonly Lazy<string> TimingHash = new(() => new PasswordHasher<User>().HashPassword(TimingUser, "remote-wake"));

    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var auth = app.MapGroup("/api/auth").RequireRateLimiting(RateLimits.Auth);
        auth.MapGet("/registration", GetRegistrationStatusAsync).DisableRateLimiting();
        auth.MapPost("/register", RegisterAsync);
        auth.MapPost("/login", LoginAsync);
        auth.MapPost("/logout", LogoutAsync).DisableRateLimiting();
        auth.MapGet("/me", GetCurrentUserAsync).RequireAuthorization().DisableRateLimiting();
    }

    private static async Task<IResult> GetRegistrationStatusAsync(AppDbContext database, IConfiguration configuration)
    {
        var setupRequired = !await database.Users.AnyAsync();
        return Results.Ok(new RegistrationStatus(setupRequired || configuration.GetValue<bool>("Registration:Open"), setupRequired));
    }

    private static async Task<IResult> RegisterAsync(RegisterRequest request, HttpContext context, AppDbContext database,
        IPasswordHasher<User> hasher, SessionService sessions, SecurityLog log, SetupCode setup,
        IConfiguration configuration, TimeProvider time)
    {
        var name = request.Name?.Trim() ?? string.Empty;
        var email = AccountRules.NormalizeEmail(request.Email);
        if (name.Length is < 2 or > 100 || !AccountRules.IsEmail(email) || !AccountRules.IsAcceptablePassword(request.Password))
            return Results.BadRequest(new { message = "Informe nome, e-mail válido e senha com pelo menos 8 caracteres." });

        await RegistrationGate.WaitAsync();
        try
        {
            // Permission first: with registration closed, the answer must not reveal
            // whether an e-mail already has an account.
            var firstUser = !await database.Users.AnyAsync();
            if (firstUser && !setup.Matches(request.SetupToken))
                return Results.Json(new { message = "Código de configuração inválido. Ele aparece nos logs da API: docker compose logs api." },
                    statusCode: StatusCodes.Status403Forbidden);
            if (!firstUser && !configuration.GetValue<bool>("Registration:Open"))
                return Results.Json(new { message = "O cadastro está fechado. O administrador pode criar sua conta." },
                    statusCode: StatusCodes.Status403Forbidden);
            if (await database.Users.AnyAsync(user => user.Email == email))
                return Results.Conflict(new { message = "Este e-mail já está cadastrado." });

            var user = new User { Name = name, Email = email, PasswordHash = string.Empty, CreatedAt = time.GetUtcNow() };
            user.PasswordHash = hasher.HashPassword(user, request.Password!);
            database.Users.Add(user);
            log.Add(user.Id, SecurityEventTypes.Registered, context);
            await database.SaveChangesAsync();
            await sessions.StartAsync(user, context);
            return Results.Ok(new AuthResponse(AccountRules.ToResponse(user)));
        }
        finally
        {
            RegistrationGate.Release();
        }
    }

    private static async Task<IResult> LoginAsync(LoginRequest request, HttpContext context, AppDbContext database,
        IPasswordHasher<User> hasher, SessionService sessions, SecurityLog log, TimeProvider time,
        FailedLoginRecorder failedLogins, AccountLocks locks)
    {
        var email = AccountRules.NormalizeEmail(request.Email);
        if (email.Length == 0 || string.IsNullOrEmpty(request.Password) || request.Password.Length > AccountRules.MaxPasswordLength)
            return InvalidCredentials();

        var user = await database.Users.SingleOrDefaultAsync(item => item.Email == email);
        if (user is null)
        {
            hasher.VerifyHashedPassword(TimingUser, TimingHash.Value, request.Password);
            return InvalidCredentials();
        }

        var verification = hasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
        if (verification == PasswordVerificationResult.Failed)
        {
            // Recorded in the background, so this path costs the same as an unknown e-mail.
            failedLogins.Enqueue(user.Id, context);
            return InvalidCredentials();
        }
        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
            user.PasswordHash = hasher.HashPassword(user, request.Password);

        if (user.TotpSecret is not null)
        {
            // The attacker already knows the password here, so limiting per account is safe.
            using var accountLock = await locks.AcquireAsync(user.Id, context.RequestAborted);
            var since = time.GetUtcNow().AddMinutes(-15);
            var failures = await database.SecurityEvents.CountAsync(item =>
                item.UserId == user.Id && item.Type == SecurityEventTypes.TwoFactorFailed && item.CreatedAt > since);
            if (failures >= 10)
                return Results.Json(new LoginChallenge("Muitas tentativas de código. Aguarde 15 minutos.", true),
                    statusCode: StatusCodes.Status429TooManyRequests);
            if (string.IsNullOrWhiteSpace(request.Code))
                return Results.Json(new LoginChallenge("Informe o código do aplicativo autenticador.", true),
                    statusCode: StatusCodes.Status401Unauthorized);
            if (!await TwoFactor.TryConsumeAsync(user, request.Code, database, log, context, time))
            {
                log.Add(user.Id, SecurityEventTypes.TwoFactorFailed, context);
                await database.SaveChangesAsync();
                return Results.Json(new LoginChallenge("Código de verificação inválido.", true),
                    statusCode: StatusCodes.Status401Unauthorized);
            }
        }

        log.Add(user.Id, SecurityEventTypes.LoginSucceeded, context);
        await sessions.StartAsync(user, context);
        return Results.Ok(new AuthResponse(AccountRules.ToResponse(user)));
    }

    private static async Task<IResult> LogoutAsync(HttpContext context, SessionService sessions, SecurityLog log, AppDbContext database)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var userId = context.User.UserId();
            await sessions.RevokeAsync(userId, context.User.SessionIdOf());
            log.Add(userId, SecurityEventTypes.Logout, context);
            await database.SaveChangesAsync();
        }
        sessions.ClearCookie(context);
        return Results.NoContent();
    }

    private static async Task<IResult> GetCurrentUserAsync(ClaimsPrincipal principal, AppDbContext database)
    {
        var user = await database.Users.AsNoTracking().SingleAsync(item => item.Id == principal.UserId());
        return Results.Ok(AccountRules.ToResponse(user));
    }

    private static IResult InvalidCredentials() =>
        Results.Json(new { message = "E-mail ou senha incorretos." }, statusCode: StatusCodes.Status401Unauthorized);
}
