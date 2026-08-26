using Gym_Management.Data;
using Gym_Management.Domain;
using Microsoft.EntityFrameworkCore;

namespace Gym_Management.Services;

public interface ISettingsService
{
    /// <summary>Idempotently seeds all known settings with their defaults if missing.</summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task<int> GetDuplicateScanThresholdMinutesAsync(CancellationToken cancellationToken = default);

    Task<string> GetGymNameAsync(CancellationToken cancellationToken = default);

    /// <summary>IANA timezone id used for every "today"/date comparison (rule 13).</summary>
    Task<string> GetTimezoneIdAsync(CancellationToken cancellationToken = default);

    Task<int> GetLowBalanceThresholdAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<string, string>> GetAllAsync(CancellationToken cancellationToken = default);

    Task UpdateAsync(IReadOnlyDictionary<string, string> updates, CancellationToken cancellationToken = default);
}

/// <summary>
/// Typed accessor over the Setting table. Reads fall back to the documented
/// defaults when a row is missing or holds an unparseable value, so callers
/// never have to guard against absence.
/// </summary>
public class SettingsService(GymDbContext db) : ISettingsService
{
    public const string DuplicateScanThresholdMinutesKey = "DuplicateScanThresholdMinutes";
    public const string GymNameKey = "GymName";
    public const string TimezoneIdKey = "TimezoneId";
    public const string LowBalanceThresholdKey = "LowBalanceThreshold";

    public const int DefaultDuplicateScanThresholdMinutes = 15;
    public const string DefaultGymName = "Gym";
    public const string DefaultTimezoneId = "UTC";
    public const int DefaultLowBalanceThreshold = 3;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var defaults = new Dictionary<string, string>
        {
            [DuplicateScanThresholdMinutesKey] = DefaultDuplicateScanThresholdMinutes.ToString(),
            [GymNameKey] = DefaultGymName,
            [TimezoneIdKey] = DefaultTimezoneId,
            [LowBalanceThresholdKey] = DefaultLowBalanceThreshold.ToString()
        };

        var existing = await db.Settings.Select(s => s.Key).ToListAsync(cancellationToken);
        foreach (var (key, value) in defaults)
        {
            if (!existing.Contains(key))
            {
                db.Settings.Add(new Setting { Id = Guid.NewGuid(), Key = key, Value = value });
            }
        }

        if (db.ChangeTracker.HasChanges())
        {
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    public Task<int> GetDuplicateScanThresholdMinutesAsync(CancellationToken cancellationToken = default) =>
        GetIntAsync(DuplicateScanThresholdMinutesKey, DefaultDuplicateScanThresholdMinutes, cancellationToken);

    public Task<string> GetGymNameAsync(CancellationToken cancellationToken = default) =>
        GetStringAsync(GymNameKey, DefaultGymName, cancellationToken);

    public Task<string> GetTimezoneIdAsync(CancellationToken cancellationToken = default) =>
        GetStringAsync(TimezoneIdKey, DefaultTimezoneId, cancellationToken);

    public Task<int> GetLowBalanceThresholdAsync(CancellationToken cancellationToken = default) =>
        GetIntAsync(LowBalanceThresholdKey, DefaultLowBalanceThreshold, cancellationToken);

    public async Task<IReadOnlyDictionary<string, string>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var rows = await db.Settings.AsNoTracking().ToListAsync(cancellationToken);
        var map = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [DuplicateScanThresholdMinutesKey] = DefaultDuplicateScanThresholdMinutes.ToString(),
            [GymNameKey] = DefaultGymName,
            [TimezoneIdKey] = DefaultTimezoneId,
            [LowBalanceThresholdKey] = DefaultLowBalanceThreshold.ToString()
        };

        foreach (var row in rows)
        {
            map[row.Key] = row.Value;
        }

        return map;
    }

    public async Task UpdateAsync(IReadOnlyDictionary<string, string> updates, CancellationToken cancellationToken = default)
    {
        var allowed = new HashSet<string>(StringComparer.Ordinal)
        {
            DuplicateScanThresholdMinutesKey,
            GymNameKey,
            TimezoneIdKey,
            LowBalanceThresholdKey
        };

        foreach (var (key, value) in updates)
        {
            if (!allowed.Contains(key))
            {
                throw ApiErrors.Validation($"Unknown setting key '{key}'.");
            }

            ValidateSetting(key, value);

            var row = await db.Settings.SingleOrDefaultAsync(s => s.Key == key, cancellationToken);
            if (row is null)
            {
                db.Settings.Add(new Setting { Id = Guid.NewGuid(), Key = key, Value = value });
            }
            else
            {
                row.Value = value;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static void ValidateSetting(string key, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw ApiErrors.Validation($"Setting '{key}' cannot be empty.");
        }

        switch (key)
        {
            case DuplicateScanThresholdMinutesKey:
            case LowBalanceThresholdKey:
                if (!int.TryParse(value, out var n) || n < 0)
                {
                    throw ApiErrors.Validation($"Setting '{key}' must be a non-negative integer.");
                }

                break;
            case TimezoneIdKey:
                try
                {
                    _ = TimeZoneInfo.FindSystemTimeZoneById(value);
                }
                catch (Exception)
                {
                    throw ApiErrors.Validation($"Unknown IANA/Windows timezone id '{value}'.");
                }

                break;
        }
    }

    private async Task<string> GetStringAsync(string key, string fallback, CancellationToken cancellationToken)
    {
        var value = await db.Settings.AsNoTracking()
            .Where(s => s.Key == key)
            .Select(s => s.Value)
            .FirstOrDefaultAsync(cancellationToken);
        return value is { Length: > 0 } ? value : fallback;
    }

    private async Task<int> GetIntAsync(string key, int fallback, CancellationToken cancellationToken)
    {
        var value = await GetStringAsync(key, string.Empty, cancellationToken);
        return int.TryParse(value, out var parsed) ? parsed : fallback;
    }
}
