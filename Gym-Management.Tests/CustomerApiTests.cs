using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Gym_Management.Tests;

[Collection("Api")]
public class CustomerApiTests(GymApiFixture fixture)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private HttpClient Client => fixture.Factory.CreateClient();

    private async Task<string> StaffTokenAsync()
    {
        var response = await Client.PostAsJsonAsync("/api/auth/login",
            new { username = fixture.Factory.TestStaffUsername, password = fixture.Factory.TestStaffPassword });
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        return payload.GetProperty("token").GetString()!;
    }

    private HttpClient Authed(string token)
    {
        var client = fixture.Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    [Fact]
    public async Task Create_Customer_Returns_Raw_Token_Once_And_List_Finds_By_Phone()
    {
        var client = Authed(await StaffTokenAsync());
        var phone = $"555-{Random.Shared.Next(1000, 9999)}";

        var create = await client.PostAsJsonAsync("/api/customers", new
        {
            firstName = "Ada",
            lastName = "Lovelace",
            phone,
            notes = "VIP"
        });
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);
        var created = await create.Content.ReadFromJsonAsync<JsonElement>(Json);
        var token = created.GetProperty("token").GetString();
        Assert.False(string.IsNullOrWhiteSpace(token));
        var id = created.GetProperty("id").GetGuid();

        var list = await client.GetAsync($"/api/customers?query={phone}");
        list.EnsureSuccessStatusCode();
        var page = await list.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.True(page.GetProperty("total").GetInt32() >= 1);

        var detail = await client.GetAsync($"/api/customers/{id}");
        detail.EnsureSuccessStatusCode();
        var body = await detail.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.Equal("Ada", body.GetProperty("firstName").GetString());
        Assert.Equal(token, body.GetProperty("token").GetString());
        Assert.False(body.TryGetProperty("tokenHash", out _));
    }

    [Fact]
    public async Task Archive_And_Token_Reset_Work()
    {
        var client = Authed(await StaffTokenAsync());
        var create = await client.PostAsJsonAsync("/api/customers", new
        {
            firstName = "Grace",
            lastName = "Hopper",
            phone = $"555-{Random.Shared.Next(1000, 9999)}"
        });
        create.EnsureSuccessStatusCode();
        var created = await create.Content.ReadFromJsonAsync<JsonElement>(Json);
        var id = created.GetProperty("id").GetGuid();
        var oldToken = created.GetProperty("token").GetString()!;

        var archive = await client.PostAsync($"/api/customers/{id}/archive", null);
        archive.EnsureSuccessStatusCode();
        var archived = await archive.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.False(archived.GetProperty("isActive").GetBoolean());

        var reset = await client.PostAsync($"/api/customers/{id}/token/reset", null);
        reset.EnsureSuccessStatusCode();
        var resetBody = await reset.Content.ReadFromJsonAsync<JsonElement>(Json);
        var newToken = resetBody.GetProperty("token").GetString();
        Assert.NotEqual(oldToken, newToken);
    }
}
