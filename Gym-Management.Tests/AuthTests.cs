using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Gym_Management.Tests;

/// <summary>Auth contract tests against the in-process API backed by a real LocalDB database.</summary>
[Collection("Api")]
public class AuthTests(GymApiFixture fixture)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private HttpClient Client => fixture.Factory.CreateClient();

    private static async Task<string> ReadReasonAsync(HttpResponseMessage response)
    {
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        if (document.RootElement.TryGetProperty("reason", out var reason))
        {
            return reason.GetString() ?? string.Empty;
        }

        return string.Empty;
    }

    private async Task<(string Token, string Role, string FullName)> LoginAsync(string username, string password)
    {
        var response = await Client.PostAsJsonAsync("/api/auth/login", new { username, password });
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<LoginPayload>(Json);
        Assert.NotNull(payload);
        return (payload!.Token, payload.Role, payload.FullName);
    }

    private sealed record LoginPayload(string Token, string Role, string FullName);

    [Fact]
    public async Task Seeded_Admin_Login_Succeeds_And_Token_Works_On_Me()
    {
        var (token, role, fullName) = await LoginAsync(fixture.Factory.TestAdminUsername, fixture.Factory.TestAdminPassword);

        Assert.Equal("Admin", role);
        Assert.NotEmpty(fullName);
        Assert.NotEmpty(token);

        using var meRequest = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        meRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var meResponse = await Client.SendAsync(meRequest);

        Assert.Equal(HttpStatusCode.OK, meResponse.StatusCode);
        var me = await meResponse.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.Equal(fixture.Factory.TestAdminUsername, me.GetProperty("username").GetString());
        Assert.Equal("Admin", me.GetProperty("role").GetString());
        Assert.True(me.GetProperty("id").GetGuid() != Guid.Empty);
        Assert.Equal(fullName, me.GetProperty("fullName").GetString());
    }

    [Fact]
    public async Task Login_With_Wrong_Password_Returns_401_With_Reason_Unauthorized()
    {
        var response = await Client.PostAsJsonAsync("/api/auth/login",
            new { username = fixture.Factory.TestAdminUsername, password = "wrong-password" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("unauthorized", await ReadReasonAsync(response));
    }

    [Fact]
    public async Task Login_With_Unknown_Username_Returns_401_With_Reason_Unauthorized()
    {
        var response = await Client.PostAsJsonAsync("/api/auth/login",
            new { username = "no-such-user", password = "whatever" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("unauthorized", await ReadReasonAsync(response));
    }

    [Fact]
    public async Task Login_With_Missing_Fields_Returns_422_With_Reason_Validation()
    {
        var response = await Client.PostAsJsonAsync("/api/auth/login", new { username = "only-username" });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("validation", await ReadReasonAsync(response));
    }

    [Fact]
    public async Task Me_Anonymous_Returns_401_With_Reason_Unauthorized()
    {
        var response = await Client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("unauthorized", await ReadReasonAsync(response));
    }

    [Fact]
    public async Task Admin_Only_Endpoint_Rejects_Staff_Token_With_403_Reason_Forbidden()
    {
        var (staffToken, staffRole, _) = await LoginAsync(fixture.Factory.TestStaffUsername, fixture.Factory.TestStaffPassword);
        Assert.Equal("Staff", staffRole);

        using var staffRequest = new HttpRequestMessage(HttpMethod.Get, "/api/test/admin-only");
        staffRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", staffToken);
        var staffResponse = await Client.SendAsync(staffRequest);

        Assert.Equal(HttpStatusCode.Forbidden, staffResponse.StatusCode);
        Assert.Equal("forbidden", await ReadReasonAsync(staffResponse));

        // The same endpoint accepts an Admin token — proves it is the role gate, not the route.
        var (adminToken, _, _) = await LoginAsync(fixture.Factory.TestAdminUsername, fixture.Factory.TestAdminPassword);
        using var adminRequest = new HttpRequestMessage(HttpMethod.Get, "/api/test/admin-only");
        adminRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        var adminResponse = await Client.SendAsync(adminRequest);

        Assert.Equal(HttpStatusCode.OK, adminResponse.StatusCode);
    }
}
