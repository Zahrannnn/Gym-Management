using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Gym_Management.Data;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Gym_Management.Tests;

/// <summary>FR-006: N parallel check-ins against 1 remaining session → exactly 1 granted.</summary>
[Collection("Api")]
public class CheckInConcurrencyTests(GymApiFixture fixture)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Parallel_Checkins_With_One_Session_Grant_Exactly_Once()
    {
        var login = fixture.Factory.CreateClient();
        var adminLogin = await login.PostAsJsonAsync("/api/auth/login",
            new { username = fixture.Factory.TestAdminUsername, password = fixture.Factory.TestAdminPassword });
        adminLogin.EnsureSuccessStatusCode();
        var adminToken = (await adminLogin.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("token").GetString()!;

        var staffLogin = await login.PostAsJsonAsync("/api/auth/login",
            new { username = fixture.Factory.TestStaffUsername, password = fixture.Factory.TestStaffPassword });
        staffLogin.EnsureSuccessStatusCode();
        var staffToken = (await staffLogin.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("token").GetString()!;

        var admin = fixture.Factory.CreateClient();
        admin.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        var staff = fixture.Factory.CreateClient();
        staff.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", staffToken);

        // Disable duplicate-scan window so parallel denials are about sessions, not duplicates.
        var settingsPut = await admin.PutAsJsonAsync("/api/settings", new Dictionary<string, string>
        {
            ["DuplicateScanThresholdMinutes"] = "0"
        });
        settingsPut.EnsureSuccessStatusCode();

        var planRes = await admin.PostAsJsonAsync("/api/plans", new
        {
            name = $"Conc-{Guid.NewGuid():N}"[..16],
            type = "Session",
            price = 10
        });
        planRes.EnsureSuccessStatusCode();
        var planId = (await planRes.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetGuid();

        var custRes = await staff.PostAsJsonAsync("/api/customers", new
        {
            firstName = "Race",
            lastName = "Condition",
            phone = $"555-{Random.Shared.Next(1000, 9999)}"
        });
        custRes.EnsureSuccessStatusCode();
        var cust = await custRes.Content.ReadFromJsonAsync<JsonElement>(Json);
        var customerId = cust.GetProperty("id").GetGuid();
        var rawToken = cust.GetProperty("token").GetString()!;

        var start = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var subRes = await staff.PostAsJsonAsync("/api/subscriptions", new
        {
            customerId,
            planId,
            startDate = start,
            totalSessions = 1
        });
        subRes.EnsureSuccessStatusCode();
        var subId = (await subRes.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetGuid();

        const int parallel = 12;
        var tasks = Enumerable.Range(0, parallel).Select(async _ =>
        {
            var client = fixture.Factory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", staffToken);
            var response = await client.PostAsJsonAsync("/api/checkins", new { token = rawToken });
            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
            return body.GetProperty("result").GetString()!;
        });

        var results = await Task.WhenAll(tasks);
        var granted = results.Count(r => r == "granted");
        var denied = results.Count(r => r == "denied");

        Assert.Equal(1, granted);
        Assert.Equal(parallel - 1, denied);

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GymDbContext>();
        var sub = await db.Subscriptions.FindAsync(subId);
        Assert.NotNull(sub);
        Assert.Equal(sub!.TotalSessions, sub.UsedSessions);
        Assert.True(sub.UsedSessions >= 0);
        Assert.Equal(1, sub.UsedSessions);
    }
}
