using System.Diagnostics.CodeAnalysis;
namespace Takeaway.Api.Services;

public interface IDemoUserStore
{
    bool TryValidateCredentials(string username, string password, [NotNullWhen(true)] out DemoUser? user);
    IReadOnlyCollection<DemoUser> Users { get; }
}

public sealed record DemoUser(string Username, string Role, string? DisplayName)
{
    public bool IsInRole(string role) => string.Equals(Role, role, StringComparison.OrdinalIgnoreCase);
}
