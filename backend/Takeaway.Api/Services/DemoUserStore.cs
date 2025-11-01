using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Takeaway.Api.Options;

namespace Takeaway.Api.Services;

public sealed class DemoUserStore : IDemoUserStore
{
    private readonly IReadOnlyDictionary<string, StoredUser> _users;
    private readonly ILogger<DemoUserStore> _logger;

    public DemoUserStore(IOptions<DemoAuthenticationOptions> options, ILogger<DemoUserStore> logger)
    {
        _logger = logger;
        _users = options.Value.DemoUsers
            .Where(u => u.IsValid())
            .GroupBy(u => u.Username, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToDictionary(
                u => u.Username,
                u => new StoredUser(u.Username, u.PasswordHash, u.Role, u.DisplayName),
                StringComparer.OrdinalIgnoreCase);

        if (_users.Count == 0)
        {
            _logger.LogWarning("Demo user store has no configured users.");
        }
    }

    public IReadOnlyCollection<DemoUser> Users => _users.Values
        .Select(u => new DemoUser(u.Username, u.Role, u.DisplayName))
        .ToArray();

    public bool TryValidateCredentials(string username, string password, out DemoUser? user)
    {
        user = null;

        if (!_users.TryGetValue(username, out var stored))
        {
            _logger.LogWarning("Invalid login attempt for user {Username}", username);
            return false;
        }

        if (!VerifyPassword(password, stored.PasswordHash))
        {
            _logger.LogWarning("Invalid password for user {Username}", username);
            return false;
        }

        user = new DemoUser(stored.Username, stored.Role, stored.DisplayName);
        return true;
    }

    private static bool VerifyPassword(string providedPassword, string storedHash)
    {
        if (string.IsNullOrWhiteSpace(storedHash))
        {
            return false;
        }

        Span<byte> providedBytes = stackalloc byte[SHA256.HashSizeInBytes];
        if (!TryHashPassword(providedPassword, providedBytes))
        {
            return false;
        }

        byte[]? storedBytes;
        try
        {
            storedBytes = Convert.FromBase64String(storedHash);
        }
        catch (FormatException)
        {
            return false;
        }

        if (storedBytes.Length != providedBytes.Length)
        {
            CryptographicOperations.ZeroMemory(providedBytes);
            return false;
        }

        var match = CryptographicOperations.FixedTimeEquals(providedBytes, storedBytes);
        CryptographicOperations.ZeroMemory(providedBytes);
        return match;
    }

    private static bool TryHashPassword(string password, Span<byte> destination)
    {
        if (password is null)
        {
            return false;
        }

        var utf8 = Encoding.UTF8.GetBytes(password);
        var hash = SHA256.HashData(utf8);
        if (hash.Length != destination.Length)
        {
            return false;
        }

        hash.CopyTo(destination);
        return true;
    }

    private sealed record StoredUser(string Username, string PasswordHash, string Role, string? DisplayName);
}
