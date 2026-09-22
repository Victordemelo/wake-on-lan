using Microsoft.EntityFrameworkCore;

namespace RemoteWake.Api.Data;

public static class SchemaUpgrades
{
    public static async Task ApplyAsync(AppDbContext database)
    {
        // Existing installations were initialized with EnsureCreated. Preserve
        // their data and apply additive, versioned upgrades transactionally.
        await using var transaction = await database.Database.BeginTransactionAsync();
        await database.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(72519001)");
        await database.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS "SchemaVersions" (
                "Version" integer PRIMARY KEY,
                "AppliedAt" timestamptz NOT NULL DEFAULT now()
            );
            ALTER TABLE "Machines" ADD COLUMN IF NOT EXISTS "AgentKeyVersion" integer NOT NULL DEFAULT 0;
            ALTER TABLE "WakeAttempts" ADD COLUMN IF NOT EXISTS "Action" text NOT NULL DEFAULT 'wake';
            INSERT INTO "SchemaVersions" ("Version") VALUES (1) ON CONFLICT DO NOTHING;
            """);
        await transaction.CommitAsync();
    }
}
