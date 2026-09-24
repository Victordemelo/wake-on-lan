using System.Net.Mail;
using Microsoft.EntityFrameworkCore;
using RemoteWake.Api.Contracts;
using RemoteWake.Api.Data;
using RemoteWake.Api.Models;

namespace RemoteWake.Api.Services;

public static class RateLimits
{
    public const string Auth = "auth";
    public const string Commands = "commands";
}

public static class AccountRules
{
    public const int MinPasswordLength = 8;
    public const int MaxPasswordLength = 256;

    public static bool IsAcceptablePassword(string? password) =>
        password is { Length: >= MinPasswordLength and <= MaxPasswordLength } && !string.IsNullOrWhiteSpace(password);

    public static bool IsEmail(string email) =>
        email.Length <= 254 && MailAddress.TryCreate(email, out var parsed) && parsed.Address == email;

    public static string NormalizeEmail(string? email) => email?.Trim().ToLowerInvariant() ?? string.Empty;

    public static UserResponse ToResponse(User user) => new(user.Id, user.Name, user.Email, user.TotpSecret is not null);
}

public static class TwoFactor
{
    // Accepts a code from the authenticator app or an unused recovery code. Both are
    // claimed with conditional updates, so two concurrent requests cannot use the same code.
    public static async Task<bool> TryConsumeAsync(User user, string? code, AppDbContext database,
        SecurityLog log, HttpContext? context, TimeProvider time)
    {
        if (user.TotpSecret is null || string.IsNullOrWhiteSpace(code)) return false;
        if (Totp.TryVerify(user.TotpSecret, code, time.GetUtcNow(), user.TotpLastStep, out var step))
        {
            var claimed = await database.Users
                .Where(item => item.Id == user.Id && (item.TotpLastStep == null || item.TotpLastStep < step))
                .ExecuteUpdateAsync(update => update.SetProperty(item => item.TotpLastStep, step));
            return claimed == 1;
        }

        if (!RecoveryCodes.LooksLikeRecoveryCode(code)) return false;
        var hash = RecoveryCodes.Hash(code);
        var used = await database.RecoveryCodes
            .Where(item => item.UserId == user.Id && item.CodeHash == hash && item.UsedAt == null)
            .ExecuteUpdateAsync(update => update.SetProperty(item => item.UsedAt, time.GetUtcNow()));
        if (used == 0) return false;
        log.Add(user.Id, SecurityEventTypes.RecoveryCodeUsed, context);
        return true;
    }

    public static async Task<IReadOnlyList<string>> ReplaceRecoveryCodesAsync(User user, AppDbContext database)
    {
        await RemoveRecoveryCodesAsync(user, database);
        var codes = RecoveryCodes.Generate();
        database.RecoveryCodes.AddRange(codes.Select(code => new RecoveryCode { UserId = user.Id, CodeHash = RecoveryCodes.Hash(code) }));
        return codes;
    }

    public static async Task DisableAsync(User user, AppDbContext database)
    {
        user.TotpSecret = null;
        user.PendingTotpSecret = null;
        user.TotpEnabledAt = null;
        user.TotpLastStep = null;
        await RemoveRecoveryCodesAsync(user, database);
    }

    private static async Task RemoveRecoveryCodesAsync(User user, AppDbContext database) =>
        database.RecoveryCodes.RemoveRange(await database.RecoveryCodes.Where(item => item.UserId == user.Id).ToListAsync());
}

// Browsers cannot add custom headers to cross-site requests without a CORS preflight,
// which this API never grants. Requiring one on state-changing calls blocks CSRF,
// including from sibling subdomains that count as "same site" for SameSite cookies.
public static class CsrfGuard
{
    public const string Header = "X-Remote-Wake-Request";

    public static IApplicationBuilder UseCsrfGuard(this IApplicationBuilder app) => app.Use(async (context, next) =>
    {
        var request = context.Request;
        var stateChanging = !(HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method) || HttpMethods.IsOptions(request.Method));
        // Workers authenticate with a key header, never with cookies.
        var workerEndpoint = request.Path.StartsWithSegments("/api/gateway") || request.Path.StartsWithSegments("/api/agent");
        if (stateChanging && request.Path.StartsWithSegments("/api") && !workerEndpoint && request.Headers[Header] != "1")
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new { message = "Requisição recusada pela proteção contra CSRF. Recarregue a página." });
            return;
        }
        await next();
    });
}
