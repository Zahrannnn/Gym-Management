using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Gym_Management.Tests;

[Collection("Api")]
public class ValidationAndCardTests(GymApiFixture fixture)
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

    private static async Task<string> ReadReasonAsync(HttpResponseMessage response)
    {
        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        return doc.RootElement.TryGetProperty("reason", out var reason)
            ? reason.GetString() ?? ""
            : "";
    }

    [Fact]
    public async Task Card_Does_Not_Rotate_Token()
    {
        var staff = await StaffClientAsync();
        var create = await staff.PostAsJsonAsync("/api/customers", new
        {
            firstName = "Card",
            lastName = "Stable",
            phone = $"555-{Random.Shared.Next(1000, 9999)}"
        });
        create.EnsureSuccessStatusCode();
        var created = await create.Content.ReadFromJsonAsync<JsonElement>(Json);
        var id = created.GetProperty("id").GetGuid();
        var token = created.GetProperty("token").GetString()!;

        var card = await staff.GetAsync($"/api/customers/{id}/card");
        card.EnsureSuccessStatusCode();
        var cardBody = await card.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.Equal("Card Stable", cardBody.GetProperty("customerName").GetString());
        Assert.Equal(token, cardBody.GetProperty("token").GetString());
        Assert.False(string.IsNullOrWhiteSpace(cardBody.GetProperty("gymName").GetString()));

        // Original token still works on public status (card did not rotate).
        var anon = fixture.Factory.CreateClient();
        var status = await anon.GetAsync($"/api/public/status/{token}");
        Assert.Equal(HttpStatusCode.OK, status.StatusCode);

        var list = await staff.GetAsync("/api/customers?query=Card");
        list.EnsureSuccessStatusCode();
        var page = await list.Content.ReadFromJsonAsync<JsonElement>(Json);
        var match = page.GetProperty("items").EnumerateArray()
            .First(i => i.GetProperty("id").GetGuid() == id);
        Assert.Equal(token, match.GetProperty("token").GetString());
    }

    [Fact]
    public async Task Post_Customer_Blank_Name_Returns_422_Validation_With_Field_Errors()
    {
        var staff = await StaffClientAsync();
        var response = await staff.PostAsJsonAsync("/api/customers", new
        {
            firstName = "   ",
            lastName = "Ok",
            phone = "555-1234"
        });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Assert.Equal("validation", doc.RootElement.GetProperty("reason").GetString());
        Assert.True(doc.RootElement.TryGetProperty("errors", out var errors));
        Assert.True(errors.EnumerateObject().Any());
    }

    [Fact]
    public async Task Post_Payment_Zero_Amount_Returns_422()
    {
        var staff = await StaffClientAsync();
        var response = await staff.PostAsJsonAsync("/api/payments", new
        {
            subscriptionId = Guid.NewGuid(),
            amount = 0,
            method = "Cash"
        });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("validation", await ReadReasonAsync(response));
    }

    [Fact]
    public async Task Post_Subscription_Empty_Guids_Returns_422()
    {
        var staff = await StaffClientAsync();
        var response = await staff.PostAsJsonAsync("/api/subscriptions", new
        {
            customerId = Guid.Empty,
            planId = Guid.Empty,
            startDate = DateOnly.FromDateTime(DateTime.UtcNow)
        });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("validation", await ReadReasonAsync(response));
    }

    [Fact]
    public async Task Post_Checkin_Blank_Token_Returns_422()
    {
        var staff = await StaffClientAsync();
        var response = await staff.PostAsJsonAsync("/api/checkins", new { token = "  " });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("validation", await ReadReasonAsync(response));
    }

    [Fact]
    public async Task Post_Cancel_Blank_Reason_Returns_422()
    {
        var staff = await StaffClientAsync();
        var response = await staff.PostAsJsonAsync($"/api/subscriptions/{Guid.NewGuid()}/cancel", new { reason = " " });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("validation", await ReadReasonAsync(response));
    }
}
