using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace RemoteWake.Api.Services;

// Time-based one-time passwords (RFC 6238): HMAC-SHA1, 30-second steps, 6 digits,
// compatible with Google Authenticator, Microsoft Authenticator, Aegis, 1Password etc.
public static class Totp
{
    private const int StepSeconds = 30;

    public static string GenerateSecret() => Base32.Encode(RandomNumberGenerator.GetBytes(20));

    public static long StepAt(DateTimeOffset time) => time.ToUnixTimeSeconds() / StepSeconds;

    public static string Code(string secret, long step)
    {
        Span<byte> counter = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(counter, step);
        var hash = HMACSHA1.HashData(Base32.Decode(secret), counter);
        var offset = hash[^1] & 0x0F;
        var value = (hash[offset] & 0x7F) << 24 | hash[offset + 1] << 16 | hash[offset + 2] << 8 | hash[offset + 3];
        return (value % 1_000_000).ToString("D6");
    }

    // Accepts one step of clock drift in each direction and never a step at or before lastStep.
    public static bool TryVerify(string secret, string? code, DateTimeOffset now, long? lastStep, out long matchedStep)
    {
        matchedStep = 0;
        var digits = code?.Replace(" ", string.Empty);
        if (digits is not { Length: 6 } || !digits.All(char.IsAsciiDigit)) return false;

        var current = StepAt(now);
        for (var step = current - 1; step <= current + 1; step++)
        {
            if (step <= lastStep) continue;
            if (CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Code(secret, step)), Encoding.ASCII.GetBytes(digits)))
            {
                matchedStep = step;
                return true;
            }
        }
        return false;
    }

    public static string ProvisioningUri(string secret, string account, string issuer = "Remote Wake") =>
        $"otpauth://totp/{Uri.EscapeDataString(issuer)}:{Uri.EscapeDataString(account)}" +
        $"?secret={secret}&issuer={Uri.EscapeDataString(issuer)}&algorithm=SHA1&digits=6&period={StepSeconds}";
}

// RFC 4648 base32 without padding, as used by otpauth:// URIs.
public static class Base32
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static string Encode(ReadOnlySpan<byte> data)
    {
        var output = new StringBuilder((data.Length * 8 + 4) / 5);
        int buffer = 0, bits = 0;
        foreach (var value in data)
        {
            buffer = buffer << 8 | value;
            bits += 8;
            while (bits >= 5)
            {
                output.Append(Alphabet[buffer >> (bits - 5) & 31]);
                bits -= 5;
            }
        }
        if (bits > 0) output.Append(Alphabet[buffer << (5 - bits) & 31]);
        return output.ToString();
    }

    public static byte[] Decode(string text)
    {
        var output = new List<byte>(text.Length * 5 / 8);
        int buffer = 0, bits = 0;
        foreach (var character in text.TrimEnd('=').ToUpperInvariant())
        {
            var index = Alphabet.IndexOf(character);
            if (index < 0) throw new FormatException("Texto base32 inválido.");
            buffer = buffer << 5 | index;
            bits += 5;
            if (bits >= 8)
            {
                output.Add((byte)(buffer >> (bits - 8)));
                bits -= 8;
            }
        }
        return [.. output];
    }
}

public static class RecoveryCodes
{
    public const int Count = 10;

    // 80 random bits each, shown as xxxx-xxxx-xxxx-xxxx. Only SHA-256 hashes are stored.
    public static IReadOnlyList<string> Generate() => Enumerable.Range(0, Count)
        .Select(_ => string.Join('-', Base32.Encode(RandomNumberGenerator.GetBytes(10)).ToLowerInvariant().Chunk(4).Select(chunk => new string(chunk))))
        .ToList();

    public static string Normalize(string code) =>
        new string(code.Where(char.IsAsciiLetterOrDigit).ToArray()).ToLowerInvariant();

    public static bool LooksLikeRecoveryCode(string? code) => code is not null && Normalize(code).Length == 16;

    public static string Hash(string code) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Normalize(code))));
}
