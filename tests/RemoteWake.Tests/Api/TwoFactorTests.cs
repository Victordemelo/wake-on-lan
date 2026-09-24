using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using RemoteWake.Api.Contracts;
using RemoteWake.Api.Services;

namespace RemoteWake.Tests.Api;

public sealed class TwoFactorTests(DefaultApi fixture) : IClassFixture<DefaultApi>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string CodeFor(string secret, int stepOffset = 0) =>
        Totp.Code(secret.Replace(" ", string.Empty), Totp.StepAt(DateTimeOffset.UtcNow) + stepOffset);

    private async Task<HttpResponseMessage> LoginAsync(string email, string? code) =>
        await ApiSession.Anonymous(fixture.Api).PostAsJsonAsync("/api/auth/login",
            new { email, password = ApiSession.DefaultPassword, code }, Ct);

    [Fact]
    public async Task Two_step_verification_protects_the_login_and_recovery_codes_work_once()
    {
        PostgresDatabase.SkipIfUnavailable();
        var email = ApiSession.NewEmail();
        var session = await ApiSession.RegisterAsync(fixture.Api, email);

        var setup = await (await session.Http.PostAsync("/api/account/two-factor/setup", null, Ct))
            .Content.ReadFromJsonAsync<TwoFactorSetupResponse>(ApiSession.Json, Ct);
        Assert.StartsWith("otpauth://totp/Remote%20Wake:", setup!.Uri);
        var wrong = await session.Http.PostAsJsonAsync("/api/account/two-factor/enable", new { code = "000000" }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        var enabled = await session.Http.PostAsJsonAsync("/api/account/two-factor/enable", new { code = CodeFor(setup.Secret) }, Ct);
        var recoveryCodes = (await enabled.Content.ReadFromJsonAsync<RecoveryCodesResponse>(ApiSession.Json, Ct))!.RecoveryCodes;
        Assert.Equal(10, recoveryCodes.Count);
        Assert.All(recoveryCodes, code => Assert.Matches("^[a-z2-7]{4}(-[a-z2-7]{4}){3}$", code));
        Assert.True((await session.GetAsync<UserResponse>("/api/auth/me")).TwoFactorEnabled);

        var withoutCode = await LoginAsync(email, null);
        Assert.Equal(HttpStatusCode.Unauthorized, withoutCode.StatusCode);
        Assert.True((await withoutCode.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("twoFactorRequired").GetBoolean());

        // The enabling code used the current step; the next step is still inside the accepted window.
        var nextCode = CodeFor(setup.Secret, stepOffset: 1);
        (await LoginAsync(email, nextCode)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(email, nextCode)).StatusCode);

        (await LoginAsync(email, recoveryCodes[0].ToUpperInvariant())).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(email, recoveryCodes[0])).StatusCode);

        var disableWithoutPassword = await session.Http.PostAsJsonAsync("/api/account/two-factor/disable",
            new { password = "Senha-errada-123", code = recoveryCodes[1] }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, disableWithoutPassword.StatusCode);
        var disabled = await session.Http.PostAsJsonAsync("/api/account/two-factor/disable",
            new { password = ApiSession.DefaultPassword, code = recoveryCodes[1] }, Ct);
        Assert.Equal(HttpStatusCode.NoContent, disabled.StatusCode);
        (await LoginAsync(email, null)).EnsureSuccessStatusCode();
    }
}

public sealed class AccountCommandTests(DefaultApi fixture) : IClassFixture<DefaultApi>
{
    private async Task<(int? ExitCode, string Output)> RunAsync(params string[] args)
    {
        using var scope = fixture.Api.Services.CreateScope();
        var output = new StringWriter();
        var original = Console.Out;
        Console.SetOut(output);
        try
        {
            return (await AccountCommands.TryRunAsync(args, scope.ServiceProvider), output.ToString());
        }
        finally
        {
            Console.SetOut(original);
        }
    }

    [Fact]
    public async Task Reset_password_prints_a_temporary_password_and_ends_every_session()
    {
        PostgresDatabase.SkipIfUnavailable();
        var email = ApiSession.NewEmail();
        var session = await ApiSession.RegisterAsync(fixture.Api, email);

        var (exitCode, output) = await RunAsync("reset-password", email.ToUpperInvariant());

        Assert.Equal(0, exitCode);
        var temporary = output.Split('\n').Single(line => line.StartsWith("Senha temporária de")).Split(": ")[1].Trim();
        Assert.Matches("^[a-z2-9]{4}(-[a-z2-9]{4}){3}$", temporary);
        Assert.Equal(HttpStatusCode.Unauthorized, (await session.Http.GetAsync("/api/auth/me", TestContext.Current.CancellationToken)).StatusCode);
        await ApiSession.LoginAsync(fixture.Api, email, temporary);
    }

    [Fact]
    public async Task Create_user_makes_an_account_that_can_sign_in()
    {
        PostgresDatabase.SkipIfUnavailable();
        var email = ApiSession.NewEmail();

        var (exitCode, output) = await RunAsync("create-user", email, "Pessoa", "da", "casa");

        Assert.Equal(0, exitCode);
        var temporary = output.Split("Senha temporária: ")[1].Split('\n')[0].Trim();
        var session = await ApiSession.LoginAsync(fixture.Api, email, temporary);
        Assert.Equal("Pessoa da casa", session.User.Name);
    }

    [Fact]
    public async Task Server_arguments_are_not_commands_and_unknown_commands_fail()
    {
        PostgresDatabase.SkipIfUnavailable();

        Assert.Null((await RunAsync()).ExitCode);
        Assert.Null((await RunAsync("--urls", "http://+:8080")).ExitCode);
        Assert.Equal(2, (await RunAsync("drop-database")).ExitCode);
        Assert.Equal(1, (await RunAsync("reset-password", "ninguem@example.test")).ExitCode);
    }
}
