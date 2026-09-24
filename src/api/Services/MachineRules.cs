using System.Net;
using System.Net.Sockets;
using RemoteWake.Api.Contracts;
using RemoteWake.Api.Models;
using RemoteWake.Shared;

namespace RemoteWake.Api.Services;

public static class MachineRules
{
    public static bool TryValidate(MachineRequest request, out string normalizedMac, out string error)
    {
        normalizedMac = string.Empty;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length is < 2 or > 100)
            error = "Informe um nome entre 2 e 100 caracteres.";
        else if (!MagicPacket.TryNormalizeMac(request.MacAddress, out normalizedMac))
            error = "Informe um endereço MAC válido, por exemplo AA:BB:CC:DD:EE:FF.";
        else if (request.WolPort is < 1 or > 65535)
            error = "A porta deve estar entre 1 e 65535.";
        else if (!Enum.IsDefined(request.WakeMethod))
            error = "Escolha um método de ativação válido.";
        else if (!IsValidDestination(request.BroadcastAddress?.Trim(), request.WakeMethod))
            error = request.WakeMethod == WakeMethod.TailscaleGateway
                ? "Informe o endereço IPv4 de broadcast da rede do gateway, por exemplo 192.168.1.255."
                : "Informe um endereço IPv4 de broadcast, IP público ou nome DDNS.";
        else if (request.Hostname?.Trim().Length > 253)
            error = "O hostname deve ter no máximo 253 caracteres.";

        return error.Length == 0;
    }

    private static bool IsValidDestination(string? destination, WakeMethod method)
    {
        if (string.IsNullOrEmpty(destination) || destination.Length > 253) return false;
        // IPAddress.TryParse also accepts shorthand forms such as "10.1"; require dotted quads.
        if (destination.Split('.').Length == 4 && IPAddress.TryParse(destination, out var address))
            return address.AddressFamily == AddressFamily.InterNetwork;
        // Digits and dots only means an incomplete IP, never a DDNS name ("10.1" would resolve to 10.0.0.1).
        if (destination.All(character => char.IsAsciiDigit(character) || character == '.')) return false;
        // The gateway only relays to IPv4 addresses present in its local allow list.
        return method != WakeMethod.TailscaleGateway && Uri.CheckHostName(destination) == UriHostNameType.Dns;
    }
}
