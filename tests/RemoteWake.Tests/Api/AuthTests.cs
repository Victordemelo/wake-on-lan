using System.Net;
using System.Net.Http.Json;

namespace RemoteWake.Tests.Api;

internal sealed record RegistrationStatus(bool Open);

public sealed class RegistrationTests(ClosedRegistrationApi fixture) : IClassFixture<ClosedRegistrationApi>
{
    [Fact]
    public async Task First_user_registers_and_registration_then_closes()
    {
        PostgresDatabase.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var anonymous = fixture.Api.CreateClient();
        Assert.True((await anonymous.GetFromJsonAsync<RegistrationStatus>("/api/auth/registration", ct))!.Open);

        await ApiSession.RegisterAsync(fixture.Api);

        Assert.False((await anonymous.GetFromJsonAsync<RegistrationStatus>("/api/auth/registration", ct))!.Open);
        var second = await anonymous.PostAsJsonAsync("/api/auth/register",
            new { name = "Outra pessoa", email = ApiSession.NewEmail(), password = "Senha-forte-123" }, ct);
        Assert.Equal(HttpStatusCode.Forbidden, second.StatusCode);
    }
}

public sealed class AuthTests(DefaultApi fixture) : IClassFixture<DefaultApi>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Login_ignores_email_case_and_surrounding_spaces()
    {
        PostgresDatabase.SkipIfUnavailable();
        var email = ApiSession.NewEmail();
        await ApiSession.RegisterAsync(fixture.Api, email, "Senha-forte-123");

        var session = await ApiSession.LoginAsync(fixture.Api, $"  {email.ToUpperInvariant()} ", "Senha-forte-123");

        Assert.Equal(email, session.User.Email);
    }

    [Fact]
    public async Task Wrong_password_and_unknown_email_look_the_same()
    {
        PostgresDatabase.SkipIfUnavailable();
        var email = ApiSession.NewEmail();
        await ApiSession.RegisterAsync(fixture.Api, email);
        var anonymous = fixture.Api.CreateClient();

        var wrongPassword = await anonymous.PostAsJsonAsync("/api/auth/login", new { email, password = "Senha-errada-123" }, Ct);
        var unknownEmail = await anonymous.PostAsJsonAsync("/api/auth/login",
            new { email = ApiSession.NewEmail(), password = "Senha-errada-123" }, Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknownEmail.StatusCode);
    }

    [Fact]
    public async Task Duplicate_email_is_rejected()
    {
        PostgresDatabase.SkipIfUnavailable();
        var email = ApiSession.NewEmail();
        await ApiSession.RegisterAsync(fixture.Api, email);

        var duplicate = await fixture.Api.CreateClient().PostAsJsonAsync("/api/auth/register",
            new { name = "Outra conta", email, password = "Senha-forte-123" }, Ct);

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
    }

    [Theory]
    [InlineData("A", "valido@example.test", "Senha-forte-123")]
    [InlineData("Nome válido", "sem-arroba", "Senha-forte-123")]
    [InlineData("Nome válido", "valido@example.test", "curta")]
    public async Task Invalid_registration_data_is_rejected(string name, string email, string password)
    {
        PostgresDatabase.SkipIfUnavailable();

        var response = await fixture.Api.CreateClient().PostAsJsonAsync("/api/auth/register", new { name, email, password }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/machines")]
    [InlineData("/api/activity")]
    public async Task Protected_endpoints_require_authentication(string path)
    {
        PostgresDatabase.SkipIfUnavailable();

        var response = await fixture.Api.CreateClient().GetAsync(path, Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
