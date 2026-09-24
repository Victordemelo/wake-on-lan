using System.Net;
using System.Net.Http.Json;

namespace RemoteWake.Tests.Api;

internal sealed record RegistrationStatus(bool Open, bool SetupRequired);

public sealed class RegistrationTests(ClosedRegistrationApi fixture) : IClassFixture<ClosedRegistrationApi>
{
    [Fact]
    public async Task First_user_registers_and_registration_then_closes()
    {
        PostgresDatabase.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var anonymous = ApiSession.Anonymous(fixture.Api);
        Assert.Equal(new RegistrationStatus(true, true), await anonymous.GetFromJsonAsync<RegistrationStatus>("/api/auth/registration", ct));

        await ApiSession.RegisterAsync(fixture.Api);

        Assert.Equal(new RegistrationStatus(false, false), await anonymous.GetFromJsonAsync<RegistrationStatus>("/api/auth/registration", ct));
        var second = await anonymous.PostAsJsonAsync("/api/auth/register",
            new { name = "Outra pessoa", email = ApiSession.NewEmail(), password = ApiSession.DefaultPassword }, ct);
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
        await ApiSession.RegisterAsync(fixture.Api, email, ApiSession.DefaultPassword);

        var session = await ApiSession.LoginAsync(fixture.Api, $"  {email.ToUpperInvariant()} ", ApiSession.DefaultPassword);

        Assert.Equal(email, session.User.Email);
    }

    [Fact]
    public async Task Wrong_password_and_unknown_email_look_the_same()
    {
        PostgresDatabase.SkipIfUnavailable();
        var email = ApiSession.NewEmail();
        await ApiSession.RegisterAsync(fixture.Api, email);
        var anonymous = ApiSession.Anonymous(fixture.Api);

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

        var duplicate = await ApiSession.Anonymous(fixture.Api).PostAsJsonAsync("/api/auth/register",
            new { name = "Outra conta", email, password = ApiSession.DefaultPassword }, Ct);

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
    }

    [Theory]
    [InlineData("A", "valido@example.test", ApiSession.DefaultPassword)]
    [InlineData("Nome válido", "sem-arroba", ApiSession.DefaultPassword)]
    [InlineData("Nome válido", "valido@example.test", "curta")]
    public async Task Invalid_registration_data_is_rejected(string name, string email, string password)
    {
        PostgresDatabase.SkipIfUnavailable();

        var response = await ApiSession.Anonymous(fixture.Api).PostAsJsonAsync("/api/auth/register", new { name, email, password }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/machines")]
    [InlineData("/api/activity")]
    public async Task Protected_endpoints_require_authentication(string path)
    {
        PostgresDatabase.SkipIfUnavailable();

        var response = await ApiSession.Anonymous(fixture.Api).GetAsync(path, Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
