using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Gym_Management.Tests;

[Collection("Api")]
public class CheckInApiTests(GymApiFixture fixture)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private async Task<HttpClient> StaffClientAsync()
    {
        var login = fixture.Factory.CreateClient();
        var r = await login.PostAsJsonAsync("/api/auth/login",
            new { username = fixture.Factory.TestStaffUsername, password = fixture.Factory.TestStaffPassword });
        r.EnsureSuccessStatusCode();
        var token = (await r.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("token").GetString()!;
        var client = fixture.Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private async Task<HttpClient> AdminClientAsync()
    {
        var login = fixture.Factory.CreateClient();
        var r = await login.PostAsJsonAsync("/api/auth/login",
            new { username = fixture.Factory.TestAdminUsername, password = fixture.Factory.TestAdminPassword });
        r.EnsureSuccessStatusCode();
        var token = (await r.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("token").GetString()!;
        var client = fixture.Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    [Fact]
    public async Task Unknown_Token_Denies_With_Token_Unknown_Http_200()
    {
        var staff = await StaffClientAsync();
        var response = await staff.PostAsJsonAsync("/api/checkins", new { token = "not-a-real-token" });
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.Equal("denied", body.GetProperty("result").GetString());
        Assert.Equal("token_unknown", body.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task Time_Sub_Checkin_Grants_And_Duplicate_Denies()
    {
        var staff = await StaffClientAsync();
        var admin = await AdminClientAsync();

        await admin.PutAsJsonAsync("/api/settings", new Dictionary<string, string>
        {
            ["DuplicateScanThresholdMinutes"] = "15"
        });

        var planRes = await admin.PostAsJsonAsync("/api/plans", new
        {
            name = $"Chk-{Guid.NewGuid():N}"[..16],
            type = "Time",
            durationDays = 30,
            price = 40
        });
        planRes.EnsureSuccessStatusCode();
        var planId = (await planRes.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetGuid();

        var custRes = await staff.PostAsJsonAsync("/api/customers", new
        {
            firstName = "Check",
            lastName = "In",
            phone = $"555-{Random.Shared.Next(1000, 9999)}"
        });
        custRes.EnsureSuccessStatusCode();
        var cust = await custRes.Content.ReadFromJsonAsync<JsonElement>(Json);
        var customerId = cust.GetProperty("id").GetGuid();
        var token = cust.GetProperty("token").GetString()!;

        var start = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var subRes = await staff.PostAsJsonAsync("/api/subscriptions", new { customerId, planId, startDate = start });
        subRes.EnsureSuccessStatusCode();

        var first = await staff.PostAsJsonAsync("/api/checkins", new { token });
        first.EnsureSuccessStatusCode();
        var granted = await first.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.Equal("granted", granted.GetProperty("result").GetString());

        var second = await staff.PostAsJsonAsync("/api/checkins", new { token });
        second.EnsureSuccessStatusCode();
        var denied = await second.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.Equal("denied", denied.GetProperty("result").GetString());
        Assert.Equal("duplicate_scan", denied.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task Archived_Customer_Denies_Customer_Inactive()
    {
        var staff = await StaffClientAsync();
        var custRes = await staff.PostAsJsonAsync("/api/customers", new
        {
            firstName = "Archived",
            lastName = "User",
            phone = $"555-{Random.Shared.Next(1000, 9999)}"
        });
        custRes.EnsureSuccessStatusCode();
        var cust = await custRes.Content.ReadFromJsonAsync<JsonElement>(Json);
        var id = cust.GetProperty("id").GetGuid();
        var token = cust.GetProperty("token").GetString()!;

        await staff.PostAsync($"/api/customers/{id}/archive", null);

        var check = await staff.PostAsJsonAsync("/api/checkins", new { token });
        check.EnsureSuccessStatusCode();
        var body = await check.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.Equal("denied", body.GetProperty("result").GetString());
        Assert.Equal("customer_inactive", body.GetProperty("reason").GetString());
    }
}
