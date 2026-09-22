using RemoteWake.Api.Models;

namespace RemoteWake.Api.Contracts;

public sealed record RegisterRequest(string Name, string Email, string Password);
public sealed record LoginRequest(string Email, string Password);
public sealed record AuthResponse(string Token, UserResponse User);
public sealed record UserResponse(Guid Id, string Name, string Email);

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
    DateTimeOffset CreatedAt);

public sealed record WakeResponse(bool Succeeded, string Message, DateTimeOffset RequestedAt);

