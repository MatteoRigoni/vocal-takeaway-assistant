using System;

namespace Takeaway.Api.Contracts.Auth;

public sealed record LoginResponse(
    string AccessToken,
    DateTime ExpiresAtUtc,
    string Role,
    string Username,
    string? DisplayName);

public sealed record CurrentUserResponse(string Username, string Role);
