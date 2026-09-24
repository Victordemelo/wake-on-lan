using System.Text;
using Microsoft.Extensions.Configuration;
using RemoteWake.Api.Services;

namespace RemoteWake.Tests.Unit;

public sealed class TotpTests
{
    // RFC 6238, appendix B (SHA-1 seed "12345678901234567890"); the last six digits of each value.
    private static readonly string Secret = Base32.Encode(Encoding.ASCII.GetBytes("12345678901234567890"));

    [Theory]
    [InlineData(59, "287082")]
    [InlineData(1111111109, "081804")]
    [InlineData(1111111111, "050471")]
    [InlineData(1234567890, "005924")]
    [InlineData(2000000000, "279037")]
    [InlineData(20000000000, "353130")]
    public void Matches_the_rfc_6238_test_vectors(long unixTime, string expected)
    {
        Assert.Equal(expected, Totp.Code(Secret, Totp.StepAt(DateTimeOffset.FromUnixTimeSeconds(unixTime))));
    }

    [Fact]
    public void Base32_round_trips_and_matches_rfc_4648()
    {
        Assert.Equal("GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ", Secret);
        Assert.Equal("MZXW6YTBOI", Base32.Encode("foobar"u8));
        var random = Convert.FromHexString("00FF10AB7C9E");
        Assert.Equal(random, Base32.Decode(Base32.Encode(random)));
        Assert.Throws<FormatException>(() => Base32.Decode("not base32!"));
    }

    [Fact]
    public void Accepts_one_step_of_clock_drift_but_no_more()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);
        var step = Totp.StepAt(now);

        Assert.True(Totp.TryVerify(Secret, Totp.Code(Secret, step - 1), now, null, out _));
        Assert.True(Totp.TryVerify(Secret, Totp.Code(Secret, step + 1), now, null, out var matched));
        Assert.Equal(step + 1, matched);
        Assert.False(Totp.TryVerify(Secret, Totp.Code(Secret, step - 2), now, null, out _));
        Assert.False(Totp.TryVerify(Secret, Totp.Code(Secret, step + 2), now, null, out _));
    }

    [Fact]
    public void A_code_cannot_be_used_again()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);
        var code = Totp.Code(Secret, Totp.StepAt(now));

        Assert.True(Totp.TryVerify(Secret, code, now, null, out var used));
        Assert.False(Totp.TryVerify(Secret, code, now, used, out _));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("12345")]
    [InlineData("1234567")]
    [InlineData("12a456")]
    public void Malformed_codes_are_rejected(string? code)
    {
        Assert.False(Totp.TryVerify(Secret, code, DateTimeOffset.UtcNow, null, out _));
    }

    [Fact]
    public void Provisioning_uri_is_understood_by_authenticator_apps()
    {
        var uri = Totp.ProvisioningUri("JBSWY3DPEHPK3PXP", "voce@exemplo.com");

        Assert.Equal("otpauth://totp/Remote%20Wake:voce%40exemplo.com?secret=JBSWY3DPEHPK3PXP&issuer=Remote%20Wake&algorithm=SHA1&digits=6&period=30", uri);
    }
}

public sealed class AccountSecretsTests
{
    [Fact]
    public void Recovery_codes_are_unique_readable_and_hashed_case_insensitively()
    {
        var codes = RecoveryCodes.Generate();

        Assert.Equal(RecoveryCodes.Count, codes.Distinct().Count());
        Assert.All(codes, code => Assert.Matches("^[a-z2-7]{4}(-[a-z2-7]{4}){3}$", code));
        Assert.Equal(RecoveryCodes.Hash(codes[0]), RecoveryCodes.Hash(" " + codes[0].ToUpperInvariant().Replace("-", " ") + " "));
        Assert.True(RecoveryCodes.LooksLikeRecoveryCode(codes[0]));
        Assert.False(RecoveryCodes.LooksLikeRecoveryCode("123456"));
    }

    [Fact]
    public void Session_tokens_are_random_and_only_their_hash_is_stored()
    {
        var first = SessionTokens.Create();
        var second = SessionTokens.Create();

        Assert.NotEqual(first, second);
        Assert.Equal(43, first.Length);
        Assert.Matches("^[0-9A-F]{64}$", SessionTokens.Hash(first));
        Assert.NotEqual(SessionTokens.Hash(first), SessionTokens.Hash(second));
    }

    [Fact]
    public void Setup_code_is_generated_when_not_configured_and_compared_loosely()
    {
        var generated = new SetupCode(new ConfigurationBuilder().Build());
        var configured = new SetupCode(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Setup:Token"] = "Abcd-2345" }).Build());

        Assert.False(generated.IsConfigured);
        Assert.Matches("^[A-HJ-NP-Z2-9]{4}-[A-HJ-NP-Z2-9]{4}-[A-HJ-NP-Z2-9]{4}$", generated.Value);
        Assert.True(generated.Matches(generated.Value.ToLowerInvariant().Replace("-", " ")));
        Assert.True(configured.IsConfigured);
        Assert.True(configured.Matches("abcd2345"));
        Assert.False(configured.Matches("abcd2346"));
        Assert.False(configured.Matches(null));
    }
}
