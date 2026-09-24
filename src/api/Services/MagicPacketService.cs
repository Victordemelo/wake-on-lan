using System.Net;
using System.Net.Sockets;
using RemoteWake.Shared;

namespace RemoteWake.Api.Services;

public sealed class MagicPacketService
{
    public async Task SendAsync(
        string macAddress,
        string destination,
        int port,
        CancellationToken cancellationToken)
    {
        var packet = MagicPacket.Build(macAddress);

        if (!IPAddress.TryParse(destination, out var address))
        {
            var addresses = await Dns.GetHostAddressesAsync(destination, cancellationToken);
            address = addresses.FirstOrDefault(item => item.AddressFamily == AddressFamily.InterNetwork)
                ?? throw new ArgumentException("Destino IPv4 ou DDNS inválido.", nameof(destination));
        }

        using var client = new UdpClient(AddressFamily.InterNetwork);
        client.EnableBroadcast = true;
        await client.SendAsync(packet, new IPEndPoint(address, port), cancellationToken);
    }
}
