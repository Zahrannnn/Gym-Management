using System.Security.Claims;

namespace Gym_Management.Services;

public static class StaffContext
{
    public static Guid GetStaffId(ClaimsPrincipal user)
    {
        var raw = user.FindFirstValue(ClaimTypes.NameIdentifier)
                  ?? user.FindFirstValue("sub");
        if (!Guid.TryParse(raw, out var id))
        {
            throw ApiErrors.Unauthorized();
        }

        return id;
    }
}
