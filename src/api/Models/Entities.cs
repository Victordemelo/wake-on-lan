namespace RemoteWake.Api.Models;

public sealed class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
    public required string Email { get; set; }
    public required string PasswordHash { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? PasswordChangedAt { get; set; }
    // Base32 TOTP secret; null while two-step verification is disabled.
    public string? TotpSecret { get; set; }
    // Secret shown during setup, kept until the user confirms a code.
    public string? PendingTotpSecret { get; set; }
    public DateTimeOffset? TotpEnabledAt { get; set; }
    // Last accepted 30-second step, so a code cannot be used twice.
    public long? TotpLastStep { get; set; }
    public ICollection<Machine> Machines { get; set; } = [];
    public ICollection<UserSession> Sessions { get; set; } = [];
    public ICollection<RecoveryCode> RecoveryCodes { get; set; } = [];
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

// History of wake and power requests. It outlives the machine: MachineId becomes
// null when the machine is removed and MachineName keeps the name it had.
public sealed class WakeAttempt
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? MachineId { get; set; }
    public Machine? Machine { get; set; }
    public Guid OwnerId { get; set; }
    public string MachineName { get; set; } = string.Empty;
    public bool Succeeded { get; set; }
    public string Action { get; set; } = "wake";
    public required string Message { get; set; }
    public DateTimeOffset RequestedAt { get; set; } = DateTimeOffset.UtcNow;
    public string? IpAddress { get; set; }
}

// Server-side login session. The cookie carries a random token; only its
// SHA-256 hash is stored, so a database leak does not allow forging sessions.
public sealed class UserSession
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public required string TokenHash { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
}

public sealed class RecoveryCode
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public required string CodeHash { get; set; }
    public DateTimeOffset? UsedAt { get; set; }
}

public sealed class SecurityEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public required string Type { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
}

public static class SecurityEventTypes
{
    public const string Registered = "registered";
    public const string LoginSucceeded = "login_succeeded";
    public const string LoginFailed = "login_failed";
    public const string TwoFactorFailed = "two_factor_failed";
    public const string RecoveryCodeUsed = "recovery_code_used";
    public const string Logout = "logout";
    public const string SessionRevoked = "session_revoked";
    public const string OtherSessionsRevoked = "other_sessions_revoked";
    public const string PasswordChanged = "password_changed";
    public const string PasswordReset = "password_reset";
    public const string TwoFactorEnabled = "two_factor_enabled";
    public const string TwoFactorDisabled = "two_factor_disabled";
    public const string RecoveryCodesRegenerated = "recovery_codes_regenerated";
}
