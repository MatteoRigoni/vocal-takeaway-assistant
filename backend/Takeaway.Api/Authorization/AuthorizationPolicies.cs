using Microsoft.Extensions.DependencyInjection;
using Takeaway.Api.Domain.Constants;

namespace Takeaway.Api.Authorization;

public static class AuthorizationPolicies
{
    public const string ViewMenu = "Policies.ViewMenu";
    public const string ViewCustomers = "Policies.ViewCustomers";
    public const string ManageCustomers = "Policies.ManageCustomers";
    public const string ViewOrders = "Policies.ViewOrders";
    public const string ManageOrders = "Policies.ManageOrders";
    public const string ManageKitchen = "Policies.ManageKitchen";
    public const string VoiceAutomation = "Policies.VoiceAutomation";

    public static IServiceCollection AddTakeawayAuthorization(this IServiceCollection services)
    {
        var builder = services.AddAuthorizationBuilder();

        builder.AddPolicy(ViewMenu, policy =>
            policy.RequireRole(UserRoles.Admin, UserRoles.Cashier, UserRoles.Kitchen, UserRoles.ReadOnly));

        builder.AddPolicy(ViewCustomers, policy =>
            policy.RequireRole(UserRoles.Admin, UserRoles.Cashier, UserRoles.ReadOnly));

        builder.AddPolicy(ManageCustomers, policy =>
            policy.RequireRole(UserRoles.Admin, UserRoles.Cashier));

        builder.AddPolicy(ViewOrders, policy =>
            policy.RequireRole(UserRoles.Admin, UserRoles.Cashier, UserRoles.Kitchen, UserRoles.ReadOnly));

        builder.AddPolicy(ManageOrders, policy =>
            policy.RequireRole(UserRoles.Admin, UserRoles.Cashier));

        builder.AddPolicy(ManageKitchen, policy =>
            policy.RequireRole(UserRoles.Admin, UserRoles.Kitchen));

        builder.AddPolicy(VoiceAutomation, policy =>
            policy.RequireRole(UserRoles.Admin, UserRoles.Cashier));

        return services;
    }
}
