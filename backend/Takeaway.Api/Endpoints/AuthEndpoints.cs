using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.IdentityModel.Tokens;
using Takeaway.Api.Contracts.Auth;
using Takeaway.Api.Options;
using Takeaway.Api.Services;

namespace Takeaway.Api.Endpoints;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/auth");

        group.MapPost("/login",
            Results<Ok<LoginResponse>, BadRequest<string>, UnauthorizedHttpResult> (
                LoginRequest request,
                IDemoUserStore userStore,
                IOptions<JwtOptions> jwtOptions,
                IDateTimeProvider clock) =>
            {
                if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
                {
                    return TypedResults.BadRequest("Username and password are required.");
                }

                if (!userStore.TryValidateCredentials(request.Username, request.Password, out var user))
                {
                    return TypedResults.Unauthorized();
                }

                var options = jwtOptions.Value;
                var now = clock.UtcNow;
                var expires = now.Add(options.AccessTokenLifetime);
                var claims = new List<Claim>
                {
                    new(JwtRegisteredClaimNames.Sub, user.Username),
                    new(JwtRegisteredClaimNames.UniqueName, user.Username),
                    new(ClaimTypes.Name, user.DisplayName ?? user.Username),
                    new(ClaimTypes.Role, user.Role),
                    new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
                };

                var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.SigningKey));
                var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

                var token = new JwtSecurityToken(
                    issuer: options.Issuer,
                    audience: options.Audience,
                    claims: claims,
                    notBefore: now,
                    expires: expires,
                    signingCredentials: credentials);

                var tokenValue = new JwtSecurityTokenHandler().WriteToken(token);

                return TypedResults.Ok(new LoginResponse(
                    tokenValue,
                    expires,
                    user.Role,
                    user.Username,
                    user.DisplayName));
            })
        .WithName("Login")
        .WithSummary("Authenticate with demo credentials and obtain a JWT access token.");

        group.MapGet("/me",
            (ClaimsPrincipal principal) =>
            {
                if (principal.Identity?.IsAuthenticated is not true)
                {
                    return TypedResults.Unauthorized();
                }

                var username = principal.Identity.Name ?? principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? "unknown";
                var role = principal.FindFirstValue(ClaimTypes.Role) ?? "unknown";
                return TypedResults.Ok(new CurrentUserResponse(username, role));
            })
        .WithName("GetCurrentUser")
        .RequireAuthorization()
        .WithSummary("Return the authenticated user's identity information.");

        return app;
    }
}
