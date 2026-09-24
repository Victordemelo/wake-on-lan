using System.Net;
using System.Net.Http.Json;

namespace RemoteWake.Tests.Api;

// The fixture allows two authentication attempts per client IP per minute.
public sealed class ForwardedHeadersTests(StrictRateLimitApi fixture) : IClassFixture<StrictRateLimitApi>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<HttpStatusCode> LoginAsync(string peer, string? forwardedFor)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new { email = "ninguem@example.test", password = "Senha-errada-123" })
        };
        request.Headers.Add(TestPeerStartupFilter.Header, peer);
        if (forwardedFor is not null) request.Headers.Add("X-Forwarded-For", forwardedFor);
        return (await fixture.Api.CreateClient().SendAsync(request, Ct)).StatusCode;
    }

    [Fact]
    public async Task Clients_behind_a_trusted_proxy_are_limited_individually()
    {
        PostgresDatabase.SkipIfUnavailable();
        const string proxy = "172.18.0.2";

        Assert.Equal(HttpStatusCode.Unauthorized, await LoginAsync(proxy, "203.0.113.1"));
        Assert.Equal(HttpStatusCode.Unauthorized, await LoginAsync(proxy, "203.0.113.1"));
        Assert.Equal(HttpStatusCode.TooManyRequests, await LoginAsync(proxy, "203.0.113.1"));
        Assert.Equal(HttpStatusCode.Unauthorized, await LoginAsync(proxy, "203.0.113.2"));
    }

    [Fact]
    public async Task Forwarded_addresses_from_untrusted_peers_are_ignored()
    {
        PostgresDatabase.SkipIfUnavailable();
        const string attacker = "198.51.100.77";

        Assert.Equal(HttpStatusCode.Unauthorized, await LoginAsync(attacker, "10.0.0.1"));
        Assert.Equal(HttpStatusCode.Unauthorized, await LoginAsync(attacker, "10.0.0.2"));
        Assert.Equal(HttpStatusCode.TooManyRequests, await LoginAsync(attacker, "10.0.0.3"));
    }

    [Fact]
    public async Task Registration_status_does_not_consume_the_login_limit()
    {
        PostgresDatabase.SkipIfUnavailable();
        var client = fixture.Api.CreateClient();
        client.DefaultRequestHeaders.Add(TestPeerStartupFilter.Header, "198.51.100.88");

        for (var request = 0; request < 5; request++)
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/registration", Ct)).StatusCode);
    }
}
