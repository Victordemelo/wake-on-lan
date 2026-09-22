using System.Security.Cryptography;
using System.Text;

namespace RemoteWake.Api.Services;

public sealed class WorkerKeys(IConfiguration configuration)
{
    public bool GatewayConfigured => !string.IsNullOrWhiteSpace(configuration["Gateway:Key"]);
    public string? GatewayOwnerEmail => configuration["Gateway:OwnerEmail"]?.Trim().ToLowerInvariant();
    public bool AgentConfigured => !string.IsNullOrWhiteSpace(configuration["Agent:MasterKey"]);

    public bool IsGateway(string? supplied) => GatewayConfigured && Matches(
        configuration["Gateway:Key"]!, supplied);

    public string? AgentKey(Guid machineId, int version = 0)
    {
        var master = configuration["Agent:MasterKey"];
        if (string.IsNullOrWhiteSpace(master)) return null;
        var data = version == 0 ? machineId.ToByteArray() : Encoding.UTF8.GetBytes($"{machineId}:v{version}");
        return Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(master), data));
    }

    public bool IsAgent(Guid machineId, int version, string? supplied)
    {
        var expected = AgentKey(machineId, version);
        return expected is not null && Matches(expected, supplied);
    }

    private static bool Matches(string expected, string? supplied)
    {
        if (string.IsNullOrEmpty(supplied)) return false;
        return CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(expected)),
            SHA256.HashData(Encoding.UTF8.GetBytes(supplied)));
    }
}
