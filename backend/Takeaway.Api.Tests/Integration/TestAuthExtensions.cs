using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Takeaway.Api.Contracts.Auth;

namespace Takeaway.Api.Tests.Integration;

public static class TestAuthExtensions
{
    public static async Task AuthenticateAsAsync(this HttpClient client, TestWebApplicationFactory factory, string username, string password)
    {
        var token = await CreateTokenAsync(factory, username, password);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    public static async Task<string> CreateTokenAsync(TestWebApplicationFactory factory, string username, string password)
    {
        using var authClient = factory.CreateClient();
        var response = await authClient.PostAsJsonAsync("/auth/login", new LoginRequest(username, password));
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<LoginResponse>();
        if (payload is null)
        {
            throw new InvalidOperationException("Login response payload is null");
        }

        return payload.AccessToken;
    }
}
