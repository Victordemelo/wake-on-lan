using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace RemoteWake.Api.Data;

public static class DatabaseMigrator
{
    public static async Task MigrateAsync(AppDbContext database, ILogger logger, CancellationToken cancellationToken = default)
    {
        await database.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            // Session-level lock: serializes schema changes if several API instances start at once.
            await database.Database.ExecuteSqlRawAsync("SELECT pg_advisory_lock(72519001)", cancellationToken);
            try
            {
                if (await IsLegacyDatabaseAsync(database, cancellationToken))
                {
                    logger.LogWarning("Banco criado por uma versão sem migrações; registrando o esquema atual como linha de base.");
                    await BaselineLegacyDatabaseAsync(database, cancellationToken);
                }
                await database.Database.MigrateAsync(cancellationToken);
            }
            finally
            {
                await database.Database.ExecuteSqlRawAsync("SELECT pg_advisory_unlock(72519001)", CancellationToken.None);
            }
        }
        finally
        {
            await database.Database.CloseConnectionAsync();
        }
    }

    // Versions up to 0.1 created the schema with EnsureCreated, which leaves no migration history.
    private static Task<bool> IsLegacyDatabaseAsync(AppDbContext database, CancellationToken cancellationToken) =>
        database.Database.SqlQueryRaw<bool>("""
            SELECT to_regclass('public."Users"') IS NOT NULL
               AND to_regclass('public."__EFMigrationsHistory"') IS NULL AS "Value"
            """).SingleAsync(cancellationToken);

    private static async Task BaselineLegacyDatabaseAsync(AppDbContext database, CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        // Bring installations that predate the additive "v1" upgrade to the shape of the
        // first migration, then record that migration as already applied.
        await database.Database.ExecuteSqlRawAsync("""
            ALTER TABLE "Machines" ADD COLUMN IF NOT EXISTS "AgentKeyVersion" integer NOT NULL DEFAULT 0;
            ALTER TABLE "Machines" ALTER COLUMN "AgentKeyVersion" DROP DEFAULT;
            ALTER TABLE "WakeAttempts" ADD COLUMN IF NOT EXISTS "Action" text NOT NULL DEFAULT 'wake';
            ALTER TABLE "WakeAttempts" ALTER COLUMN "Action" DROP DEFAULT;
            DROP TABLE IF EXISTS "SchemaVersions";
            """, cancellationToken);
        var history = database.GetService<IHistoryRepository>();
        var baseline = database.Database.GetMigrations().First();
        await database.Database.ExecuteSqlRawAsync(history.GetCreateIfNotExistsScript(), cancellationToken);
        await database.Database.ExecuteSqlRawAsync(
            history.GetInsertScript(new HistoryRow(baseline, ProductInfo.GetVersion())), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
