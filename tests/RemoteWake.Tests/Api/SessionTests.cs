using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using RemoteWake.Api.Contracts;
using RemoteWake.Api.Services;

namespace RemoteWake.Tests.Api;

public sealed class SessionTests(DefaultApi fixture) : IClassFixture<DefaultApi>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Login_sets_an_http_only_strict_cookie_and_returns_no_token()
    {
        PostgresDatabase.SkipIfUnavailable();
        var email = ApiSession.NewEmail();
        await ApiSession.RegisterAsync(fixture.Api, email);

        var response = await ApiSession.Anonymous(fixture.Api)
            .PostAsJsonAsync("/api/auth/login", new { email, password = ApiSession.DefaultPassword }, Ct);

        response.EnsureSuccessStatusCode();
        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));
        Assert.StartsWith("rw_session=", cookie);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.False(body.TryGetProperty("token", out _));
        Assert.Equal(email, body.GetProperty("user").GetProperty("email").GetString());
    }

    [Fact]
    public async Task Behind_an_https_proxy_the_cookie_is_secure_and_host_only()
    {
        PostgresDatabase.SkipIfUnavailable();
        var email = ApiSession.NewEmail();
        await ApiSession.RegisterAsync(fixture.Api, email);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new { email, password = ApiSession.DefaultPassword })
        };
        request.Headers.Add(CsrfGuard.Header, "1");
        request.Headers.Add(TestPeerStartupFilter.Header, "172.18.0.2");
        request.Headers.Add("X-Forwarded-Proto", "https");

        var response = await fixture.Api.CreateClient().SendAsync(request, Ct);

        response.EnsureSuccessStatusCode();
        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));
        Assert.StartsWith("__Host-rw_session=", cookie);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Logout_revokes_the_session_on_the_server()
    {
        PostgresDatabase.SkipIfUnavailable();
        var email = ApiSession.NewEmail();
        await ApiSession.RegisterAsync(fixture.Api, email);
        var login = await ApiSession.Anonymous(fixture.Api)
            .PostAsJsonAsync("/api/auth/login", new { email, password = ApiSession.DefaultPassword }, Ct);
        var cookie = login.Headers.GetValues("Set-Cookie").Single().Split(';')[0];

        var stolen = fixture.Api.CreateClient();
        stolen.DefaultRequestHeaders.Add("Cookie", cookie);
        stolen.DefaultRequestHeaders.Add(CsrfGuard.Header, "1");
        Assert.Equal(HttpStatusCode.OK, (await stolen.GetAsync("/api/auth/me", Ct)).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await stolen.PostAsync("/api/auth/logout", null, Ct)).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await stolen.GetAsync("/api/auth/me", Ct)).StatusCode);
    }

    [Fact]
    public async Task Other_devices_can_be_signed_out()
    {
        PostgresDatabase.SkipIfUnavailable();
        var email = ApiSession.NewEmail();
        var laptop = await ApiSession.RegisterAsync(fixture.Api, email);
        var phone = await ApiSession.LoginAsync(fixture.Api, email, ApiSession.DefaultPassword);

        var sessions = await laptop.GetAsync<List<SessionResponse>>("/api/account/sessions");
        Assert.Equal(2, sessions.Count);
        Assert.Single(sessions, session => session.Current);

        (await laptop.Http.PostAsync("/api/account/sessions/revoke-others", null, Ct)).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.Unauthorized, (await phone.Http.GetAsync("/api/auth/me", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await laptop.Http.GetAsync("/api/auth/me", Ct)).StatusCode);
    }

    [Fact]
    public async Task Changing_the_password_requires_the_current_one_and_signs_out_other_devices()
    {
        PostgresDatabase.SkipIfUnavailable();
        var email = ApiSession.NewEmail();
        var laptop = await ApiSession.RegisterAsync(fixture.Api, email);
        var phone = await ApiSession.LoginAsync(fixture.Api, email, ApiSession.DefaultPassword);

        var wrong = await laptop.Http.PostAsJsonAsync("/api/account/password",
            new { currentPassword = "Senha-errada-123", newPassword = "Nova-senha-456" }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        var changed = await laptop.Http.PostAsJsonAsync("/api/account/password",
            new { currentPassword = ApiSession.DefaultPassword, newPassword = "Nova-senha-456" }, Ct);
        changed.EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.Unauthorized, (await phone.Http.GetAsync("/api/auth/me", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await laptop.Http.GetAsync("/api/auth/me", Ct)).StatusCode);
        var oldPassword = await ApiSession.Anonymous(fixture.Api)
            .PostAsJsonAsync("/api/auth/login", new { email, password = ApiSession.DefaultPassword }, Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, oldPassword.StatusCode);
        await ApiSession.LoginAsync(fixture.Api, email, "Nova-senha-456");
    }

    [Fact]
    public async Task Security_events_record_what_happened_to_the_account()
    {
        PostgresDatabase.SkipIfUnavailable();
        var email = ApiSession.NewEmail();
        var session = await ApiSession.RegisterAsync(fixture.Api, email);
        await ApiSession.Anonymous(fixture.Api).PostAsJsonAsync("/api/auth/login", new { email, password = "Senha-errada-123" }, Ct);
        await ApiSession.LoginAsync(fixture.Api, email, ApiSession.DefaultPassword);

        // Wrong passwords are recorded in the background.
        SecurityOverview overview = null!;
        for (var attempt = 0; attempt < 50; attempt++)
        {
            overview = await session.GetAsync<SecurityOverview>("/api/account/events");
            if (overview.FailedPasswords == 1) break;
            await Task.Delay(100, Ct);
        }

        Assert.Equal(1, overview.FailedPasswords);
        Assert.NotNull(overview.LastFailedPassword);
        Assert.Equal(["login_succeeded", "registered"], overview.Events.Select(item => item.Type));
        Assert.All(overview.Events, item => Assert.Equal("198.51.100.1", item.IpAddress));
    }

    [Fact]
    public async Task Over_https_only_the_host_only_cookie_is_accepted()
    {
        PostgresDatabase.SkipIfUnavailable();
        var email = ApiSession.NewEmail();
        await ApiSession.RegisterAsync(fixture.Api, email);
        var login = await ApiSession.Anonymous(fixture.Api)
            .PostAsJsonAsync("/api/auth/login", new { email, password = ApiSession.DefaultPassword }, Ct);
        var plainCookie = login.Headers.GetValues("Set-Cookie").Single().Split(';')[0];

        async Task<HttpStatusCode> MeAsync(bool https)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
            request.Headers.Add("Cookie", plainCookie);
            request.Headers.Add(TestPeerStartupFilter.Header, "172.18.0.2");
            if (https) request.Headers.Add("X-Forwarded-Proto", "https");
            return (await fixture.Api.CreateClient().SendAsync(request, Ct)).StatusCode;
        }

        Assert.Equal(HttpStatusCode.OK, await MeAsync(https: false));
        Assert.Equal(HttpStatusCode.Unauthorized, await MeAsync(https: true));
    }
}

public sealed class SetupAndCsrfTests(ClosedRegistrationApi fixture) : IClassFixture<ClosedRegistrationApi>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_first_account_needs_the_setup_code_and_state_changes_need_the_csrf_header()
    {
        PostgresDatabase.SkipIfUnavailable();
        var browser = ApiSession.Anonymous(fixture.Api);
        object Account(string? setupToken) => new { name = "Dona da casa", email = "dona@example.test", password = ApiSession.DefaultPassword, setupToken };

        Assert.Equal(HttpStatusCode.Forbidden, (await browser.PostAsJsonAsync("/api/auth/register", Account(null), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await browser.PostAsJsonAsync("/api/auth/register", Account("WRONG-CODE-0000"), Ct)).StatusCode);

        var forged = fixture.Api.CreateClient();
        var withoutHeader = await forged.PostAsJsonAsync("/api/auth/register", Account(ApiFactory.SetupToken), Ct);
        Assert.Equal(HttpStatusCode.Forbidden, withoutHeader.StatusCode);
        Assert.Contains("CSRF", (await withoutHeader.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("message").GetString());

        var owner = await browser.PostAsJsonAsync("/api/auth/register", Account("test-setup-code"), Ct);
        owner.EnsureSuccessStatusCode();

        var session = new ApiSession(browser, (await owner.Content.ReadFromJsonAsync<AuthResponse>(ApiSession.Json, Ct))!.User);
        var cookie = owner.Headers.GetValues("Set-Cookie").Single().Split(';')[0];
        var crossSite = fixture.Api.CreateClient();
        crossSite.DefaultRequestHeaders.Add("Cookie", cookie);
        Assert.Equal(HttpStatusCode.Forbidden, (await crossSite.PostAsJsonAsync("/api/machines", ApiSession.Machine(), Ct)).StatusCode);
        Assert.Empty(await session.GetAsync<List<MachineResponse>>("/api/machines"));
        await session.CreateMachineAsync();
    }
}
