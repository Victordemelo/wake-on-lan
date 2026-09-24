using System.Net;
using System.Net.Sockets;

namespace RemoteWake.Shared;

// Contract exchanged by the API and the workers (gateway and agent) over long polling.
public sealed record RemoteJob(Guid Id, Guid MachineId, string Action, string? MacAddress = null,
    string? BroadcastAddress = null, int WolPort = 9)
{
    public DateTimeOffset ExpiresAt { get; init; }
}

public sealed record RemoteJobResult(bool Succeeded, string Message);

public static class RemoteActions
{
    public const string Wake = "wake";
    public const string Shutdown = "shutdown";
    public const string Restart = "restart";
    public const string Suspend = "suspend";
    public const string Hibernate = "hibernate";
    // Recorded by the API when an agent comes online shortly after a wake request.
    public const string Online = "online";

    // Power actions an agent may execute. Arbitrary commands are never accepted.
    public static IReadOnlyList<string> Power { get; } = [Shutdown, Restart, Suspend, Hibernate];

    public static bool IsPowerAction(string? action) => action is not null && Power.Contains(action);

    // Sleep states take effect at once, so agents run them only after reporting the result.
    public static bool IsSleep(string? action) => action is Suspend or Hibernate;
}

public static class PowerCommand
{
    // Shutdown and restart keep the OS grace period, so the result is reported before
    // the machine goes down; the agent itself delays suspend and hibernate.
    public static (string FileName, string[] Arguments) For(string action, bool windows) => (action, windows) switch
    {
        (RemoteActions.Shutdown, true) => ("shutdown.exe", ["/s", "/t", "30"]),
        (RemoteActions.Restart, true) => ("shutdown.exe", ["/r", "/t", "30"]),
        // "rundll32 powrprof.dll,SetSuspendState" hibernates when hibernation is enabled; .NET suspends.
        (RemoteActions.Suspend, true) => ("powershell.exe", ["-NoProfile", "-NonInteractive", "-Command",
            "Add-Type -AssemblyName System.Windows.Forms; if (-not [System.Windows.Forms.Application]::SetSuspendState('Suspend', $false, $false)) { exit 1 }"]),
        (RemoteActions.Hibernate, true) => ("shutdown.exe", ["/h"]),
        (RemoteActions.Shutdown, false) => ("shutdown", ["-h", "+1"]),
        (RemoteActions.Restart, false) => ("shutdown", ["-r", "+1"]),
        (RemoteActions.Suspend, false) => ("systemctl", ["suspend"]),
        (RemoteActions.Hibernate, false) => ("systemctl", ["hibernate"]),
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Ação não permitida.")
    };
}

public static class GatewayAllowList
{
    public static IReadOnlyList<string> Parse(string? value) =>
        (value ?? string.Empty).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    // The gateway only sends to IPv4 destinations explicitly configured on the device.
    public static bool Permits(IReadOnlyList<string> allowed, string? destination, int port, out IPAddress address)
    {
        address = IPAddress.None;
        if (destination is null || port is < 1 or > 65535
            || !allowed.Contains(destination, StringComparer.OrdinalIgnoreCase)
            || !IPAddress.TryParse(destination, out var parsed)
            || parsed.AddressFamily != AddressFamily.InterNetwork)
            return false;
        address = parsed;
        return true;
    }
}

public static class WorkerSettings
{
    // The API may live under a path (https://example.com/remotewake/). Without the final
    // slash, relative requests such as "api/gateway/poll" would drop the last segment.
    public static Uri? ParseApiUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var text = value.Trim();
        if (!text.EndsWith('/')) text += "/";
        return Uri.TryCreate(text, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" ? uri : null;
    }
}
