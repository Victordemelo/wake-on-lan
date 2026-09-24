using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using RemoteWake.Api.Data;
using RemoteWake.Api.Models;

namespace RemoteWake.Api.Services;

// Records wrong-password attempts in the background. Writing them during the request
// would make a known e-mail measurably slower than an unknown one (account enumeration).
// Per account at most 100 are kept per day, and failures expire after 90 days.
public sealed class FailedLoginRecorder(IServiceScopeFactory scopes, TimeProvider time, ILogger<FailedLoginRecorder> logger)
    : BackgroundService
{
    private const int DailyLimitPerAccount = 100;
    private static readonly TimeSpan Retention = TimeSpan.FromDays(90);

    private readonly Channel<SecurityEvent> queue = Channel.CreateBounded<SecurityEvent>(
        new BoundedChannelOptions(1000) { FullMode = BoundedChannelFullMode.DropOldest });
    private DateTimeOffset nextCleanup;

    public void Enqueue(Guid userId, HttpContext context) => queue.Writer.TryWrite(new SecurityEvent
    {
        UserId = userId,
        Type = SecurityEventTypes.LoginFailed,
        CreatedAt = time.GetUtcNow(),
        IpAddress = ClientInfo.IpAddress(context),
        UserAgent = ClientInfo.UserAgent(context)
    });

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var item in queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var dayAgo = item.CreatedAt.AddDays(-1);
                var recent = await database.SecurityEvents.CountAsync(existing => existing.UserId == item.UserId
                    && existing.Type == SecurityEventTypes.LoginFailed && existing.CreatedAt > dayAgo, stoppingToken);
                if (recent < DailyLimitPerAccount)
                {
                    database.SecurityEvents.Add(item);
                    await database.SaveChangesAsync(stoppingToken);
                }
                await CleanUpAsync(database, stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(exception, "Could not record a failed login.");
            }
        }
    }

    private async Task CleanUpAsync(AppDbContext database, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        if (now < nextCleanup) return;
        nextCleanup = now.AddHours(1);
        var expired = now - Retention;
        await database.SecurityEvents
            .Where(item => (item.Type == SecurityEventTypes.LoginFailed || item.Type == SecurityEventTypes.TwoFactorFailed)
                && item.CreatedAt < expired)
            .ExecuteDeleteAsync(cancellationToken);
    }
}

// Serializes two-step verification attempts of the same account, so parallel requests
// cannot exceed the failure limit or reuse a code before it is marked as used.
public sealed class AccountLocks
{
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> locks = new();

    public async Task<IDisposable> AcquireAsync(Guid userId, CancellationToken cancellationToken)
    {
        var gate = locks.GetOrAdd(userId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        return new Release(gate);
    }

    private sealed class Release(SemaphoreSlim gate) : IDisposable
    {
        public void Dispose() => gate.Release();
    }
}
