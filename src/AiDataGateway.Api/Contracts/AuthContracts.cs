namespace AiDataGateway.Api.Contracts;

public sealed record SetupRequest(string UserName, string Email, string DisplayName, string Password, string AiClientName = "Local AI Client");
public sealed record LoginRequest(string UserName, string Password, bool RememberMe = false);
public sealed record CreateUserRequest(string UserName, string Email, string DisplayName, string Password, string[] Roles);
public sealed record UpdateUserRequest(string DisplayName, bool Enabled, string[] Roles);
public sealed record CreateOAuthClientRequest(string DisplayName, string[]? Scopes = null);
