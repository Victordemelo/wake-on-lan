using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using RemoteWake.Api.Contracts;
using RemoteWake.Api.Data;
using RemoteWake.Api.Models;
using RemoteWake.Api.Services;

namespace RemoteWake.Api.Endpoints;

public static class AccountEndpoints
{
    public static void MapAccountEndpoints(this IEndpointRouteBuilder app)
    {
        var account = app.MapGroup("/api/account").RequireAuthorization();
        account.MapPost("/password", ChangePasswordAsync).RequireRateLimiting(RateLimits.Auth);
        account.MapGet("/sessions", ListSessionsAsync);
        account.MapDelete("/sessions/{id:guid}", RevokeSessionAsync);
        account.MapPost("/sessions/revoke-others", RevokeOtherSessionsAsync);
        account.MapGet("/events", ListSecurityEventsAsync);

        var twoFactor = account.MapGroup("/two-factor").RequireRateLimiting(RateLimits.Auth);
        twoFactor.MapPost("/setup", StartTwoFactorSetupAsync);
        twoFactor.MapPost("/enable", EnableTwoFactorAsync);
        twoFactor.MapPost("/disable", DisableTwoFactorAsync);
        twoFactor.MapPost("/recovery-codes", RegenerateRecoveryCodesAsync);
    }

    private static async Task<IResult> ChangePasswordAsync(ChangePasswordRequest request, ClaimsPrincipal principal,
        HttpContext context, AppDbContext database, IPasswordHasher<User> hasher, SessionService sessions,
        SecurityLog log, TimeProvider time)
    {
        var user = await database.Users.SingleAsync(item => item.Id == principal.UserId());
        if (string.IsNullOrEmpty(request.CurrentPassword)
            || hasher.VerifyHashedPassword(user, user.PasswordHash, request.CurrentPassword) == PasswordVerificationResult.Failed)
            return Error("A senha atual está incorreta.");
        if (!AccountRules.IsAcceptablePassword(request.NewPassword))
            return Error("A nova senha deve ter entre 8 e 256 caracteres.");
        if (request.NewPassword == request.CurrentPassword)
            return Error("A nova senha deve ser diferente da atual.");

        user.PasswordHash = hasher.HashPassword(user, request.NewPassword!);
        user.PasswordChangedAt = time.GetUtcNow();
        log.Add(user.Id, SecurityEventTypes.PasswordChanged, context);
        await database.SaveChangesAsync();
        // Anyone who knew the old password is signed out everywhere else.
        var revoked = await sessions.RevokeAllAsync(user.Id, except: principal.SessionIdOf());
        return Results.Ok(new { revokedSessions = revoked });
    }

    private static async Task<IResult> ListSessionsAsync(ClaimsPrincipal principal, AppDbContext database,
        TimeProvider time, LoginSessionOptions options)
    {
        var userId = principal.UserId();
        var current = principal.SessionIdOf();
        var now = time.GetUtcNow();
        var idleSince = now - options.IdleTimeout;
        return Results.Ok(await database.UserSessions.AsNoTracking()
            .Where(item => item.UserId == userId && item.RevokedAt == null && item.ExpiresAt > now && item.LastSeenAt > idleSince)
            .OrderByDescending(item => item.LastSeenAt)
            .Select(item => new SessionResponse(item.Id, item.CreatedAt, item.LastSeenAt, item.IpAddress, item.UserAgent, item.Id == current))
            .ToListAsync());
    }

    private static async Task<IResult> RevokeSessionAsync(Guid id, ClaimsPrincipal principal, HttpContext context,
        AppDbContext database, SessionService sessions, SecurityLog log)
    {
        var userId = principal.UserId();
        if (!await sessions.RevokeAsync(userId, id)) return Results.NotFound();
        log.Add(userId, SecurityEventTypes.SessionRevoked, context);
        await database.SaveChangesAsync();
        if (id == principal.SessionIdOf()) sessions.ClearCookie(context);
        return Results.NoContent();
    }

    private static async Task<IResult> RevokeOtherSessionsAsync(ClaimsPrincipal principal, HttpContext context,
        AppDbContext database, SessionService sessions, SecurityLog log)
    {
        var userId = principal.UserId();
        var revoked = await sessions.RevokeAllAsync(userId, except: principal.SessionIdOf());
        log.Add(userId, SecurityEventTypes.OtherSessionsRevoked, context);
        await database.SaveChangesAsync();
        return Results.Ok(new { revokedSessions = revoked });
    }

    private static async Task<IResult> ListSecurityEventsAsync(ClaimsPrincipal principal, AppDbContext database, TimeProvider time)
    {
        var userId = principal.UserId();
        var mine = database.SecurityEvents.AsNoTracking().Where(item => item.UserId == userId);
        var events = await mine
            .Where(item => item.Type != SecurityEventTypes.LoginFailed && item.Type != SecurityEventTypes.TwoFactorFailed)
            .OrderByDescending(item => item.CreatedAt).Take(30)
            .Select(item => new SecurityEventResponse(item.Id, item.Type, item.CreatedAt, item.IpAddress, item.UserAgent))
            .ToListAsync();
        var since = time.GetUtcNow().AddDays(-30);
        var passwords = mine.Where(item => item.Type == SecurityEventTypes.LoginFailed && item.CreatedAt > since);
        var codes = mine.Where(item => item.Type == SecurityEventTypes.TwoFactorFailed && item.CreatedAt > since);
        return Results.Ok(new SecurityOverview(events,
            await passwords.CountAsync(), await passwords.MaxAsync(item => (DateTimeOffset?)item.CreatedAt),
            await codes.CountAsync(), await codes.MaxAsync(item => (DateTimeOffset?)item.CreatedAt)));
    }

    private static async Task<IResult> StartTwoFactorSetupAsync(ClaimsPrincipal principal, AppDbContext database)
    {
        var user = await database.Users.SingleAsync(item => item.Id == principal.UserId());
        if (user.TotpSecret is not null) return Error("A verificação em duas etapas já está ativa.");
        user.PendingTotpSecret = Totp.GenerateSecret();
        await database.SaveChangesAsync();
        var readable = string.Join(' ', user.PendingTotpSecret.Chunk(4).Select(chunk => new string(chunk)));
        return Results.Ok(new TwoFactorSetupResponse(readable, Totp.ProvisioningUri(user.PendingTotpSecret, user.Email)));
    }

    private static async Task<IResult> EnableTwoFactorAsync(TwoFactorEnableRequest request, ClaimsPrincipal principal,
        HttpContext context, AppDbContext database, IPasswordHasher<User> hasher, SecurityLog log, TimeProvider time)
    {
        var user = await database.Users.SingleAsync(item => item.Id == principal.UserId());
        if (user.TotpSecret is not null) return Error("A verificação em duas etapas já está ativa.");
        if (user.PendingTotpSecret is null) return Error("Gere um novo código QR antes de ativar.");
        // Without the password, a stolen session could lock the owner out with an attacker's authenticator.
        if (string.IsNullOrEmpty(request.Password)
            || hasher.VerifyHashedPassword(user, user.PasswordHash, request.Password) == PasswordVerificationResult.Failed)
            return Error("Senha incorreta.");
        if (!Totp.TryVerify(user.PendingTotpSecret, request.Code, time.GetUtcNow(), null, out var step))
            return Error("Código inválido. Confira se o horário do celular está correto e tente de novo.");

        user.TotpSecret = user.PendingTotpSecret;
        user.PendingTotpSecret = null;
        user.TotpEnabledAt = time.GetUtcNow();
        user.TotpLastStep = step;
        var codes = await TwoFactor.ReplaceRecoveryCodesAsync(user, database);
        log.Add(user.Id, SecurityEventTypes.TwoFactorEnabled, context);
        await database.SaveChangesAsync();
        return Results.Ok(new RecoveryCodesResponse(codes));
    }

    private static async Task<IResult> DisableTwoFactorAsync(TwoFactorDisableRequest request, ClaimsPrincipal principal,
        HttpContext context, AppDbContext database, IPasswordHasher<User> hasher, SecurityLog log, TimeProvider time)
    {
        var user = await database.Users.SingleAsync(item => item.Id == principal.UserId());
        if (user.TotpSecret is null) return Error("A verificação em duas etapas não está ativa.");
        if (string.IsNullOrEmpty(request.Password)
            || hasher.VerifyHashedPassword(user, user.PasswordHash, request.Password) == PasswordVerificationResult.Failed)
            return Error("Senha incorreta.");
        if (!await TwoFactor.TryConsumeAsync(user, request.Code, database, log, context, time))
            return Error("Código de verificação inválido.");

        await TwoFactor.DisableAsync(user, database);
        log.Add(user.Id, SecurityEventTypes.TwoFactorDisabled, context);
        await database.SaveChangesAsync();
        return Results.NoContent();
    }

    private static async Task<IResult> RegenerateRecoveryCodesAsync(PasswordConfirmation request, ClaimsPrincipal principal,
        HttpContext context, AppDbContext database, IPasswordHasher<User> hasher, SecurityLog log)
    {
        var user = await database.Users.SingleAsync(item => item.Id == principal.UserId());
        if (user.TotpSecret is null) return Error("Ative a verificação em duas etapas primeiro.");
        if (string.IsNullOrEmpty(request.Password)
            || hasher.VerifyHashedPassword(user, user.PasswordHash, request.Password) == PasswordVerificationResult.Failed)
            return Error("Senha incorreta.");

        var codes = await TwoFactor.ReplaceRecoveryCodesAsync(user, database);
        log.Add(user.Id, SecurityEventTypes.RecoveryCodesRegenerated, context);
        await database.SaveChangesAsync();
        return Results.Ok(new RecoveryCodesResponse(codes));
    }

    private static IResult Error(string message) => Results.BadRequest(new { message });
}
