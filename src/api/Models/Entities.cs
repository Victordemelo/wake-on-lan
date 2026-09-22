namespace RemoteWake.Api.Models;

public sealed class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
    public required string Email { get; set; }
    public required string PasswordHash { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public ICollection<Machine> Machines { get; set; } = [];
}

public enum WakeMethod
{
    LocalBroadcast,
    WakeOnWan,
    TailscaleGateway
}

public sealed class Machine
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
    public required string MacAddress { get; set; }
    public string? Hostname { get; set; }
    public string BroadcastAddress { get; set; } = "255.255.255.255";
    public int WolPort { get; set; } = 9;
    public WakeMethod WakeMethod { get; set; } = WakeMethod.LocalBroadcast;
    public DateTimeOffset? LastWakeRequestedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid OwnerId { get; set; }
    public int AgentKeyVersion { get; set; }
    public User? Owner { get; set; }
    public ICollection<WakeAttempt> WakeAttempts { get; set; } = [];
}

public sealed class WakeAttempt
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid MachineId { get; set; }
    public Machine? Machine { get; set; }
    public bool Succeeded { get; set; }
    public string Action { get; set; } = "wake";
    public required string Message { get; set; }
    public DateTimeOffset RequestedAt { get; set; } = DateTimeOffset.UtcNow;
}

