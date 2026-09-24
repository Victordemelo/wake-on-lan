using RemoteWake.Api.Models;

namespace RemoteWake.Api.Contracts;

public sealed record RegisterRequest(string? Name, string? Email, string? Password, string? SetupToken);
public sealed record LoginRequest(string? Email, string? Password, string? Code);
public sealed record UserResponse(Guid Id, string Name, string Email, bool TwoFactorEnabled);
public sealed record AuthResponse(UserResponse User);
public sealed record LoginChallenge(string Message, bool TwoFactorRequired);
public sealed record RegistrationStatus(bool Open, bool SetupRequired);

public sealed record ChangePasswordRequest(string? CurrentPassword, string? NewPassword);
public sealed record PasswordConfirmation(string? Password);
public sealed record SessionResponse(Guid Id, DateTimeOffset CreatedAt, DateTimeOffset LastSeenAt,
    string? IpAddress, string? UserAgent, bool Current);
public sealed record SecurityEventResponse(Guid Id, string Type, DateTimeOffset CreatedAt, string? IpAddress, string? UserAgent);
public sealed record TwoFactorSetupResponse(string Secret, string Uri);
public sealed record TwoFactorCodeRequest(string? Code);
public sealed record TwoFactorDisableRequest(string? Password, string? Code);
public sealed record RecoveryCodesResponse(IReadOnlyList<string> RecoveryCodes);

public sealed record MachineRequest(
    string Name,
    string MacAddress,
    string? Hostname,
    string BroadcastAddress,
    int WolPort,
    WakeMethod WakeMethod);

public sealed record MachineResponse(
    Guid Id,
    string Name,
    string MacAddress,
    string? Hostname,
    string BroadcastAddress,
    int WolPort,
    WakeMethod WakeMethod,
    DateTimeOffset? LastWakeRequestedAt,
    DateTimeOffset CreatedAt,
    bool AgentOnline,
    bool GatewayOnline);

public sealed record ActivityResponse(Guid Id, Guid? MachineId, string MachineName, string Action,
    bool Succeeded, string Message, DateTimeOffset RequestedAt);

public sealed record WakeResponse(bool Succeeded, string Message, DateTimeOffset RequestedAt);
public sealed record ActionRequest(string? Action);
