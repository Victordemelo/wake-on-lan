using System.Buffers.Text;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RemoteWake.Api.Data;
using RemoteWake.Api.Models;

namespace RemoteWake.Api.Services;

public sealed record LoginSessionOptions
{
    // Over HTTPS the cookie is Secure and uses the __Host- prefix, which also stops
    // subdomains from setting or overriding it. Plain HTTP (development, LAN) keeps
    // working with a regular HttpOnly cookie.
    public const string SecureCookieName = "__Host-rw_session";
    public const string PlainCookieName = "rw_session";

    public TimeSpan IdleTimeout { get; init; } = TimeSpan.FromDays(7);
    public TimeSpan AbsoluteLifetime { get; init; } = TimeSpan.FromDays(30);
    // For TLS proxies that do not send X-Forwarded-Proto: always issue the Secure cookie.
    public bool AlwaysSecure { get; init; }

    public bool IsSecure(HttpContext context) => AlwaysSecure || context.Request.IsHttps;

    // Over HTTPS only the __Host- cookie counts: a sibling subdomain could plant a plain
    // cookie for the parent domain and sign the victim into the attacker's account.
    public string? ReadToken(HttpContext context) => IsSecure(context)
        ? context.Request.Cookies[SecureCookieName]
        : context.Request.Cookies[PlainCookieName];
}

public static class SessionTokens
{
    public static string Create() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

    public static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}

public static class ClientInfo
{
    public static string? IpAddress(HttpContext context)
    {
        var address = context.Connection.RemoteIpAddress;
        if (address is null) return null;
        return (address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address).ToString();
    }

    public static string? UserAgent(HttpContext context)
    {
        var value = context.Request.Headers.UserAgent.ToString();
        return string.IsNullOrWhiteSpace(value) ? null : value[..Math.Min(value.Length, 256)];
    }
}

public static class UserClaims
{
    public const string SessionId = "sid";

    public static Guid UserId(this ClaimsPrincipal principal) =>
        Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);

    public static Guid SessionIdOf(this ClaimsPrincipal principal) =>
        Guid.Parse(principal.FindFirstValue(SessionId)!);
}

public sealed class SessionService(AppDbContext database, TimeProvider time, LoginSessionOptions options)
{
    public async Task StartAsync(User user, HttpContext context)
    {
        var token = SessionTokens.Create();
        var now = time.GetUtcNow();
        var session = new UserSession
        {
            UserId = user.Id,
            TokenHash = SessionTokens.Hash(token),
            CreatedAt = now,
            LastSeenAt = now,
            ExpiresAt = now + options.AbsoluteLifetime,
            IpAddress = ClientInfo.IpAddress(context),
            UserAgent = ClientInfo.UserAgent(context)
        };
        database.UserSessions.Add(session);
        await database.SaveChangesAsync();
        var secure = options.IsSecure(context);
        context.Response.Cookies.Append(secure ? LoginSessionOptions.SecureCookieName : LoginSessionOptions.PlainCookieName, token,
            new CookieOptions
            {
                HttpOnly = true,
                Secure = secure,
                SameSite = SameSiteMode.Strict,
                Path = "/",
                Expires = session.ExpiresAt,
                IsEssential = true
            });
    }

    public void ClearCookie(HttpContext context) => ClearCookies(context);

    public static void ClearCookies(HttpContext context)
    {
        if (context.Request.Cookies.ContainsKey(LoginSessionOptions.SecureCookieName))
            context.Response.Cookies.Delete(LoginSessionOptions.SecureCookieName,
                new CookieOptions { HttpOnly = true, Secure = true, SameSite = SameSiteMode.Strict, Path = "/" });
        if (context.Request.Cookies.ContainsKey(LoginSessionOptions.PlainCookieName))
            context.Response.Cookies.Delete(LoginSessionOptions.PlainCookieName,
                new CookieOptions { HttpOnly = true, SameSite = SameSiteMode.Strict, Path = "/" });
    }

    // Revokes every active session of the user except, optionally, the current one.
    public Task<int> RevokeAllAsync(Guid userId, Guid? except = null)
    {
        var now = time.GetUtcNow();
        var sessions = database.UserSessions.Where(session => session.UserId == userId && session.RevokedAt == null);
        if (except is Guid keep) sessions = sessions.Where(session => session.Id != keep);
        return sessions.ExecuteUpdateAsync(update => update.SetProperty(session => session.RevokedAt, now));
    }

    public async Task<bool> RevokeAsync(Guid userId, Guid sessionId)
    {
        var now = time.GetUtcNow();
        return await database.UserSessions
            .Where(session => session.Id == sessionId && session.UserId == userId && session.RevokedAt == null)
            .ExecuteUpdateAsync(update => update.SetProperty(session => session.RevokedAt, now)) > 0;
    }
}

public sealed class SessionAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> schemeOptions,
    ILoggerFactory logger,
    UrlEncoder encoder,
    AppDbContext database,
    TimeProvider time,
    LoginSessionOptions options)
    : AuthenticationHandler<AuthenticationSchemeOptions>(schemeOptions, logger, encoder)
{
    public const string SchemeName = "RemoteWakeSession";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var token = options.ReadToken(Context);
        if (string.IsNullOrEmpty(token) || token.Length > 128) return AuthenticateResult.NoResult();

        var hash = SessionTokens.Hash(token);
        var now = time.GetUtcNow();
        var session = await database.UserSessions.Include(item => item.User)
            .SingleOrDefaultAsync(item => item.TokenHash == hash, Context.RequestAborted);
        if (session is null || session.RevokedAt is not null || session.ExpiresAt <= now
            || now - session.LastSeenAt > options.IdleTimeout)
            return AuthenticateResult.Fail("Sessão expirada ou encerrada.");

        // Sliding idle timeout, written at most once a minute to spare the database.
        if (now - session.LastSeenAt > TimeSpan.FromMinutes(1))
        {
            session.LastSeenAt = now;
            session.IpAddress = ClientInfo.IpAddress(Context);
            await database.SaveChangesAsync(Context.RequestAborted);
        }

        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, session.UserId.ToString()),
            new Claim(UserClaims.SessionId, session.Id.ToString()),
            new Claim(ClaimTypes.Name, session.User!.Name),
            new Claim(ClaimTypes.Email, session.User.Email)
        ], SchemeName);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        SessionService.ClearCookies(Context);
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Response.WriteAsJsonAsync(new { message = "Sua sessão expirou. Entre novamente." });
    }

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    }
}

// Generated on every start while no account exists, unless SETUP_TOKEN is configured.
// Whoever creates the first account must read it from the API logs.
public sealed class SetupCode
{
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private readonly byte[] expectedHash;

    public SetupCode(IConfiguration configuration)
    {
        var configured = configuration["Setup:Token"];
        IsConfigured = !string.IsNullOrWhiteSpace(configured);
        Value = IsConfigured ? configured!.Trim() : string.Join('-', Enumerable.Range(0, 3)
            .Select(_ => new string(Enumerable.Range(0, 4).Select(_ => Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)]).ToArray())));
        expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(Normalize(Value)));
    }

    public string Value { get; }
    public bool IsConfigured { get; }

    public bool Matches(string? candidate) => candidate is not null && CryptographicOperations.FixedTimeEquals(
        expectedHash, SHA256.HashData(Encoding.UTF8.GetBytes(Normalize(candidate))));

    private static string Normalize(string value) => new string(value.Where(char.IsAsciiLetterOrDigit).ToArray()).ToUpperInvariant();
}

public sealed class SecurityLog(AppDbContext database, TimeProvider time)
{
    public void Add(Guid userId, string type, HttpContext? context) => database.SecurityEvents.Add(new SecurityEvent
    {
        UserId = userId,
        Type = type,
        CreatedAt = time.GetUtcNow(),
        IpAddress = context is null ? null : ClientInfo.IpAddress(context),
        UserAgent = context is null ? "linha de comando" : ClientInfo.UserAgent(context)
    });
}
