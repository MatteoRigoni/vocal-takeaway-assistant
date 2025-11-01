using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Takeaway.Api.Contracts.Customers;
using Xunit;

namespace Takeaway.Api.Tests.Integration;

public class CustomerEndpointsTests : IAsyncLifetime
{
    private readonly TestWebApplicationFactory _factory = new();
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _client = _factory.CreateClient();
        await _client.AuthenticateAsAsync(_factory, "admin", "Admin123!");
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await Task.CompletedTask;
        _factory.Dispose();
    }

    [Fact]
    public async Task CrudFlow_WorksForCustomers()
    {
        var createRequest = new UpsertCustomerRequest("Mario Rossi", "+39055111222", "mario@example.com", "Via Firenze 12");
        var createResponse = await _client.PostAsJsonAsync("/customers", createRequest);
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<CustomerDto>();
        Assert.NotNull(created);

        var fetched = await _client.GetFromJsonAsync<CustomerDto>($"/customers/{created!.Id}");
        Assert.NotNull(fetched);
        Assert.Equal("Mario Rossi", fetched!.Name);

        var updateRequest = new UpsertCustomerRequest("Mario Rossi", "+39055333444", null, null);
        var updateResponse = await _client.PutAsJsonAsync($"/customers/{created.Id}", updateRequest);
        updateResponse.EnsureSuccessStatusCode();
        var updated = await updateResponse.Content.ReadFromJsonAsync<CustomerDto>();
        Assert.NotNull(updated);
        Assert.Equal("+39055333444", updated!.Phone);
        Assert.Equal(string.Empty, updated.Email);

        var deleteResponse = await _client.DeleteAsync($"/customers/{created.Id}");
        Assert.Equal(System.Net.HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var missing = await _client.GetAsync($"/customers/{created.Id}");
        Assert.Equal(System.Net.HttpStatusCode.NotFound, missing.StatusCode);
    }
}
