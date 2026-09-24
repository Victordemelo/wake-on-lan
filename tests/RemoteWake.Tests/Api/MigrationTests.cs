using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using RemoteWake.Api.Contracts;
using RemoteWake.Api.Models;

namespace RemoteWake.Tests.Api;

public sealed class MigrationTests
{
    private const string Password = "Senha-legada-123";
    private static readonly string LegacySchema = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "legacy-v1.sql"));

    [Fact]
    public async Task A_new_database_is_created_by_the_migrations()
    {
        PostgresDatabase.SkipIfUnavailable();
        await using var database = new PostgresDatabase();
        await database.InitializeAsync();
        await using var api = new ApiFactory(database.ConnectionString, new Dictionary<string, string?>());

        await ApiSession.RegisterAsync(api);

        Assert.Equal(await AppliedMigrationsAsync(database), await ExpectedMigrationsAsync(api));
    }

    [Fact]
    public async Task A_database_created_by_version_0_1_keeps_its_data()
    {
        PostgresDatabase.SkipIfUnavailable();
        await using var database = new PostgresDatabase();
        await database.InitializeAsync();
        await database.ExecuteAsync(LegacySchema);
        var (email, machineId) = await SeedLegacyDataAsync(database, withV1Columns: true);
        await using var api = new ApiFactory(database.ConnectionString, new Dictionary<string, string?>());

        var session = await ApiSession.LoginAsync(api, email, Password);

        Assert.Equal(machineId, Assert.Single(await session.GetAsync<List<MachineResponse>>("/api/machines")).Id);
        Assert.Equal("restart", Assert.Single(await session.GetAsync<List<ActivityItem>>("/api/activity")).Action);
        Assert.False(await database.ScalarAsync<bool>("""SELECT to_regclass('public."SchemaVersions"') IS NOT NULL"""));
        Assert.Equal(await AppliedMigrationsAsync(database), await ExpectedMigrationsAsync(api));
    }

    [Fact]
    public async Task A_database_from_before_the_v1_upgrade_gains_the_missing_columns()
    {
        PostgresDatabase.SkipIfUnavailable();
        await using var database = new PostgresDatabase();
        await database.InitializeAsync();
        await database.ExecuteAsync(LegacySchema);
        await database.ExecuteAsync("""
            ALTER TABLE "Machines" DROP COLUMN "AgentKeyVersion";
            ALTER TABLE "WakeAttempts" DROP COLUMN "Action";
            DROP TABLE "SchemaVersions";
            """);
        var (email, machineId) = await SeedLegacyDataAsync(database, withV1Columns: false);
        await using var api = new ApiFactory(database.ConnectionString, new Dictionary<string, string?>());

        var session = await ApiSession.LoginAsync(api, email, Password);

        Assert.Equal(machineId, Assert.Single(await session.GetAsync<List<MachineResponse>>("/api/machines")).Id);
        Assert.Equal("wake", Assert.Single(await session.GetAsync<List<ActivityItem>>("/api/activity")).Action);
        var agentKey = await session.Http.PostAsync($"/api/machines/{machineId}/agent-key", null, TestContext.Current.CancellationToken);
        agentKey.EnsureSuccessStatusCode();
    }

    private static async Task<(string Email, Guid MachineId)> SeedLegacyDataAsync(PostgresDatabase database, bool withV1Columns)
    {
        var email = ApiSession.NewEmail();
        var user = new User { Name = "Conta antiga", Email = email, PasswordHash = string.Empty };
        user.PasswordHash = new PasswordHasher<User>().HashPassword(user, Password);
        var machineId = Guid.NewGuid();

        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand($"""
            INSERT INTO "Users" ("Id", "Name", "Email", "PasswordHash", "CreatedAt")
            VALUES (@user, @name, @email, @hash, now());
            INSERT INTO "Machines" ("Id", "Name", "MacAddress", "Hostname", "BroadcastAddress", "WolPort", "WakeMethod",
                "LastWakeRequestedAt", "CreatedAt", "OwnerId"{(withV1Columns ? ", \"AgentKeyVersion\"" : "")})
            VALUES (@machine, 'PC antigo', 'AABBCCDDEEFF', NULL, '192.168.1.255', 9, 0, now(), now(), @user{(withV1Columns ? ", 0" : "")});
            INSERT INTO "WakeAttempts" ("Id", "MachineId", "Succeeded", "Message", "RequestedAt"{(withV1Columns ? ", \"Action\"" : "")})
            VALUES (@attempt, @machine, true, 'Registro antigo', now(){(withV1Columns ? ", 'restart'" : "")});
            """, connection);
        command.Parameters.AddWithValue("user", user.Id);
        command.Parameters.AddWithValue("name", user.Name);
        command.Parameters.AddWithValue("email", email);
        command.Parameters.AddWithValue("hash", user.PasswordHash);
        command.Parameters.AddWithValue("machine", machineId);
        command.Parameters.AddWithValue("attempt", Guid.NewGuid());
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        return (email, machineId);
    }

    private static async Task<List<string>> AppliedMigrationsAsync(PostgresDatabase database)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand("""SELECT "MigrationId" FROM "__EFMigrationsHistory" ORDER BY 1""", connection);
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        var migrations = new List<string>();
        while (await reader.ReadAsync(TestContext.Current.CancellationToken)) migrations.Add(reader.GetString(0));
        return migrations;
    }

    private static Task<List<string>> ExpectedMigrationsAsync(ApiFactory api)
    {
        using var scope = api.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<RemoteWake.Api.Data.AppDbContext>();
        return Task.FromResult(Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions.GetMigrations(context.Database).ToList());
    }
}
