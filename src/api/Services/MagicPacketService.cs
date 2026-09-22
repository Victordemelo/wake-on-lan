using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace RemoteWake.Api.Services;

public sealed partial class MagicPacketService
{
    [GeneratedRegex("[^0-9A-Fa-f]")]
    private static partial Regex MacSeparatorRegex();

    public static bool TryNormalizeMac(string input, out string normalized)
    {
        normalized = MacSeparatorRegex().Replace(input, string.Empty).ToUpperInvariant();
        return normalized.Length == 12 && normalized.All(Uri.IsHexDigit);
    }

    public async Task SendAsync(
        string macAddress,
        string destination,
        int port,
        CancellationToken cancellationToken)
    {
        if (!TryNormalizeMac(macAddress, out var normalized))
        {
            throw new ArgumentException("Endereço MAC inválido.", nameof(macAddress));
        }

        if (!IPAddress.TryParse(destination, out var address))
        {
            var addresses = await Dns.GetHostAddressesAsync(destination, cancellationToken);
            address = addresses.FirstOrDefault(item => item.AddressFamily == AddressFamily.InterNetwork)
                ?? throw new ArgumentException("Destino IPv4 ou DDNS inválido.", nameof(destination));
        }

        var macBytes = Convert.FromHexString(normalized);
        var packet = new byte[6 + (16 * macBytes.Length)];
        Array.Fill(packet, (byte)0xFF, 0, 6);
        for (var index = 6; index < packet.Length; index += macBytes.Length)
        {
            macBytes.CopyTo(packet, index);
        }

        using var client = new UdpClient(AddressFamily.InterNetwork);
        client.EnableBroadcast = true;
        await client.SendAsync(packet, new IPEndPoint(address, port), cancellationToken);
    }
}

