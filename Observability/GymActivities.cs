using System.Diagnostics;

namespace Gym_Management.Observability;

/// <summary>Activity sources for distributed/local tracing without extra packages.</summary>
public static class GymActivities
{
    public const string SourceName = "GymManagement";

    public static readonly ActivitySource Source = new(SourceName, "1.0.0");

    public static class Operations
    {
        public const string CheckIn = "gym.checkin";
        public const string Login = "gym.auth.login";
        public const string PublicStatus = "gym.public.status";
    }
}
