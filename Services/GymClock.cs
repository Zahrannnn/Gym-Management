namespace Gym_Management.Services;

public interface IGymClock
{
    DateTime UtcNow { get; }

    /// <summary>Calendar "today" in the gym's IANA timezone (rule 13).</summary>
    Task<DateOnly> TodayAsync(CancellationToken cancellationToken = default);
}

public class GymClock(ISettingsService settings) : IGymClock
{
    public DateTime UtcNow => DateTime.UtcNow;

    public async Task<DateOnly> TodayAsync(CancellationToken cancellationToken = default)
    {
        var timezoneId = await settings.GetTimezoneIdAsync(cancellationToken);
        TimeZoneInfo zone;
        try
        {
            zone = TimeZoneInfo.FindSystemTimeZoneById(timezoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            zone = TimeZoneInfo.Utc;
        }
        catch (InvalidTimeZoneException)
        {
            zone = TimeZoneInfo.Utc;
        }

        var local = TimeZoneInfo.ConvertTimeFromUtc(UtcNow, zone);
        return DateOnly.FromDateTime(local);
    }
}
