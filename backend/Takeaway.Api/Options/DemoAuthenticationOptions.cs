using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Takeaway.Api.Options;

public sealed class DemoAuthenticationOptions
{
    public const string SectionName = "Authentication";

    [MinLength(1)]
    public List<DemoUserOptions> DemoUsers { get; } = new();
}

public sealed class DemoUserOptions
{
    [Required]
    public string Username { get; init; } = string.Empty;

    [Required]
    public string PasswordHash { get; init; } = string.Empty;

    [Required]
    public string Role { get; init; } = string.Empty;

    public string? DisplayName { get; init; }

    public bool IsValid() =>
        !string.IsNullOrWhiteSpace(Username) &&
        !string.IsNullOrWhiteSpace(PasswordHash) &&
        !string.IsNullOrWhiteSpace(Role);
}
