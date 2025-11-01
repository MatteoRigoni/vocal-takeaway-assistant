using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Takeaway.Api.Options;
using Takeaway.Api.Services;
using Xunit;

namespace Takeaway.Api.Tests.Unit;

public class DemoUserStoreTests
{
    private static readonly DemoUserOptions AdminUser = new()
    {
        Username = "admin",
        PasswordHash = "PrP+ZrMeO00Q+nC1ytSccRIpSvauTkdqHEBRVdRaoSE=",
        Role = "Admin",
        DisplayName = "Alex"
    };

    [Fact]
    public void TryValidateCredentials_ReturnsTrueForValidUser()
    {
        var store = CreateStore(AdminUser);

        var result = store.TryValidateCredentials("admin", "Admin123!", out var user);

        Assert.True(result);
        Assert.NotNull(user);
        Assert.Equal("admin", user!.Username);
        Assert.Equal("Admin", user.Role);
    }

    [Fact]
    public void TryValidateCredentials_ReturnsFalseForInvalidPassword()
    {
        var store = CreateStore(AdminUser);

        var result = store.TryValidateCredentials("admin", "wrong", out var user);

        Assert.False(result);
        Assert.Null(user);
    }

    [Fact]
    public void Users_ReturnsConfiguredUsersWithoutDuplicates()
    {
        var options = Options.Create(new DemoAuthenticationOptions
        {
            DemoUsers =
            {
                AdminUser,
                new DemoUserOptions
                {
                    Username = "admin",
                    PasswordHash = AdminUser.PasswordHash,
                    Role = "Admin",
                    DisplayName = "Duplicate"
                },
                new DemoUserOptions
                {
                    Username = "cashier",
                    PasswordHash = "oWgOHcPVpVtv5Nkr04y/8n5fzTHZkx1RLZNvjPYO4pk=",
                    Role = "Cashier"
                }
            }
        });

        var store = new DemoUserStore(options, NullLogger<DemoUserStore>.Instance);

        Assert.Collection(store.Users,
            user => Assert.Equal("admin", user.Username),
            user => Assert.Equal("cashier", user.Username));
    }

    private static DemoUserStore CreateStore(params DemoUserOptions[] users)
    {
        var options = Options.Create(new DemoAuthenticationOptions
        {
            DemoUsers = { users }
        });

        return new DemoUserStore(options, NullLogger<DemoUserStore>.Instance);
    }
}
