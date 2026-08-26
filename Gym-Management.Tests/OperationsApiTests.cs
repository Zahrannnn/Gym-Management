using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Gym_Management.Tests;

[Collection("Api")]
public class OperationsApiTests(GymApiFixture fixture)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private async Task<(HttpClient Client, string AdminToken, string StaffToken)> AuthedClientsAsync()
    {
        var loginClient = fixture.Factory.CreateClient();

        async Task<string> Login(string user, string pass)
        {
            var r = await loginClient.PostAsJsonAsync("/api/auth/login", new { username = user, password = pass });
            r.EnsureSuccessStatusCode();
            var body = await r.Content.ReadFromJsonAsync<JsonElement>(Json);
            return body.GetProperty("token").GetString()!;
        }

        var adminToken = await Login(fixture.Factory.TestAdminUsername, fixture.Factory.TestAdminPassword);
        var staffToken = await Login(fixture.Factory.TestStaffUsername, fixture.Factory.TestStaffPassword);

        var admin = fixture.Factory.CreateClient();
        admin.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        var staff = fixture.Factory.CreateClient();
        staff.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", staffToken);
        return (staff, adminToken, staffToken);
    }

    private static HttpClient WithBearer(GymApiFactory factory, string token)
    {
        var c = factory.CreateClient();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return c;
    }

    [Fact]
    public async Task Plans_Require_Admin_To_Create_Staff_Can_List()
    {
        var (_, adminToken, staffToken) = await AuthedClientsAsync();
        var staff = WithBearer(fixture.Factory, staffToken);
        var admin = WithBearer(fixture.Factory, adminToken);

        var denied = await staff.PostAsJsonAsync("/api/plans", new
        {
            name = "Session credits",
            type = "Session",
            price = 100
        });
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        var created = await admin.PostAsJsonAsync("/api/plans", new
        {
            name = $"Plan-{Guid.NewGuid():N}"[..20],
            type = "Session",
            price = 100
        });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);

        var list = await staff.GetAsync("/api/plans");
        list.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Subscription_Payment_PublicStatus_And_Settings_Flow()
    {
        var (_, adminToken, staffToken) = await AuthedClientsAsync();
        var staff = WithBearer(fixture.Factory, staffToken);
        var admin = WithBearer(fixture.Factory, adminToken);
        var anon = fixture.Factory.CreateClient();

        var planRes = await admin.PostAsJsonAsync("/api/plans", new
        {
            name = $"Time-{Guid.NewGuid():N}"[..16],
            type = "Time",
            durationDays = 30,
            price = 50
        });
        planRes.EnsureSuccessStatusCode();
        var planId = (await planRes.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetGuid();

        var custRes = await staff.PostAsJsonAsync("/api/customers", new
        {
            firstName = "Sam",
            lastName = "Member",
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
            startDate = start
        });
        subRes.EnsureSuccessStatusCode();
        var sub = await subRes.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.Equal("Active", sub.GetProperty("status").GetString());
        var subId = sub.GetProperty("id").GetGuid();

        var payRes = await staff.PostAsJsonAsync("/api/payments", new
        {
            subscriptionId = subId,
            amount = 50,
            method = "Cash",
            note = "Paid at desk"
        });
        payRes.EnsureSuccessStatusCode();

        var publicRes = await anon.GetAsync($"/api/public/status/{rawToken}");
        publicRes.EnsureSuccessStatusCode();
        var pub = await publicRes.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.Equal("Sam M.", pub.GetProperty("customerName").GetString());
        Assert.Equal("Time", pub.GetProperty("subscriptionType").GetString());
        Assert.False(pub.TryGetProperty("phone", out _));

        var unknown = await anon.GetAsync($"/api/public/status/{Guid.NewGuid():N}");
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);

        var settingsGet = await admin.GetAsync("/api/settings");
        settingsGet.EnsureSuccessStatusCode();

        var settingsStaff = await staff.GetAsync("/api/settings");
        Assert.Equal(HttpStatusCode.Forbidden, settingsStaff.StatusCode);

        var dash = await staff.GetAsync("/api/reports/dashboard");
        dash.EnsureSuccessStatusCode();
        var dashBody = await dash.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.True(dashBody.GetProperty("activeSubscriptions").GetInt32() >= 1);
    }

    [Fact]
    public async Task Session_Plan_Omits_Fixed_Count_Subscription_Sets_TotalSessions()
    {
        var (_, adminToken, staffToken) = await AuthedClientsAsync();
        var staff = WithBearer(fixture.Factory, staffToken);
        var admin = WithBearer(fixture.Factory, adminToken);

        var badPlan = await admin.PostAsJsonAsync("/api/plans", new
        {
            name = $"Bad-{Guid.NewGuid():N}"[..16],
            type = "Session",
            sessions = 30,
            price = 50
        });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, badPlan.StatusCode);

        var planRes = await admin.PostAsJsonAsync("/api/plans", new
        {
            name = $"Credits-{Guid.NewGuid():N}"[..16],
            type = "Session",
            price = 50
        });
        planRes.EnsureSuccessStatusCode();
        var plan = await planRes.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.Equal(JsonValueKind.Null, plan.GetProperty("sessions").ValueKind);
        var planId = plan.GetProperty("id").GetGuid();

        var custRes = await staff.PostAsJsonAsync("/api/customers", new
        {
            firstName = "Flex",
            lastName = "Sessions",
            phone = $"555-{Random.Shared.Next(1000, 9999)}"
        });
        custRes.EnsureSuccessStatusCode();
        var customerId = (await custRes.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetGuid();

        var missingCount = await staff.PostAsJsonAsync("/api/subscriptions", new
        {
            customerId,
            planId,
            startDate = DateOnly.FromDateTime(DateTime.UtcNow.Date)
        });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, missingCount.StatusCode);

        var subRes = await staff.PostAsJsonAsync("/api/subscriptions", new
        {
            customerId,
            planId,
            startDate = DateOnly.FromDateTime(DateTime.UtcNow.Date),
            totalSessions = 12,
            overridePrice = 180
        });
        subRes.EnsureSuccessStatusCode();
        var sub = await subRes.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.Equal(12, sub.GetProperty("totalSessions").GetInt32());
        Assert.Equal(180, sub.GetProperty("pricePaid").GetDecimal());
    }

    [Fact]
    public async Task Overlap_Conflict_On_Second_Active_Time_Sub()
    {
        var (_, adminToken, staffToken) = await AuthedClientsAsync();
        var staff = WithBearer(fixture.Factory, staffToken);
        var admin = WithBearer(fixture.Factory, adminToken);

        var planRes = await admin.PostAsJsonAsync("/api/plans", new
        {
            name = $"Overlap-{Guid.NewGuid():N}"[..16],
            type = "Time",
            durationDays = 10,
            price = 20
        });
        planRes.EnsureSuccessStatusCode();
        var planId = (await planRes.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetGuid();

        var custRes = await staff.PostAsJsonAsync("/api/customers", new
        {
            firstName = "Overlap",
            lastName = "Case",
            phone = $"555-{Random.Shared.Next(1000, 9999)}"
        });
        custRes.EnsureSuccessStatusCode();
        var customerId = (await custRes.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetGuid();

        var start = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var first = await staff.PostAsJsonAsync("/api/subscriptions", new { customerId, planId, startDate = start });
        first.EnsureSuccessStatusCode();

        var second = await staff.PostAsJsonAsync("/api/subscriptions", new { customerId, planId, startDate = start });
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        using var doc = await JsonDocument.ParseAsync(await second.Content.ReadAsStreamAsync());
        Assert.Equal("overlap_conflict", doc.RootElement.GetProperty("reason").GetString());
    }
}
