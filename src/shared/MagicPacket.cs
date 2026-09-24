using System.Text.RegularExpressions;

namespace RemoteWake.Shared;

// A Magic Packet is six 0xFF bytes followed by the target MAC repeated 16 times.
public static partial class MagicPacket
{
    public const int Length = 102;

    // Hex digits optionally separated by ':', '-', '.' or spaces (AA:BB:.., AA-BB-.., AABB.CCDD.EEFF).
    [GeneratedRegex(@"^[0-9A-Fa-f:\-.\s]+$")]
    private static partial Regex MacInputRegex();

    [GeneratedRegex(@"[:\-.\s]")]
    private static partial Regex MacSeparatorRegex();

    public static bool TryNormalizeMac(string? input, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(input) || !MacInputRegex().IsMatch(input)) return false;
        var digits = MacSeparatorRegex().Replace(input, string.Empty).ToUpperInvariant();
        if (digits.Length != 12) return false;
        normalized = digits;
        return true;
    }

    public static byte[] Build(string macAddress)
    {
        if (!TryNormalizeMac(macAddress, out var normalized))
            throw new ArgumentException("Endereço MAC inválido.", nameof(macAddress));

        var mac = Convert.FromHexString(normalized);
        var packet = new byte[Length];
        Array.Fill(packet, (byte)0xFF, 0, 6);
        for (var offset = 6; offset < Length; offset += mac.Length)
            mac.CopyTo(packet, offset);
        return packet;
    }
}
