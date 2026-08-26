using System.ComponentModel.DataAnnotations;

namespace Gym_Management.Validation;

/// <summary>Rejects null, empty, or whitespace-only strings.</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class NotBlankAttribute : ValidationAttribute
{
    public NotBlankAttribute()
        : base("{0} is required and cannot be blank.")
    {
    }

    public override bool IsValid(object? value)
    {
        return value is string s && !string.IsNullOrWhiteSpace(s);
    }
}

/// <summary>Rejects <see cref="Guid.Empty"/> (default binding for missing Guid fields).</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class NotEmptyGuidAttribute : ValidationAttribute
{
    public NotEmptyGuidAttribute()
        : base("{0} must be a non-empty GUID.")
    {
    }

    public override bool IsValid(object? value) => value is Guid g && g != Guid.Empty;
}

/// <summary>Basic phone sanity: digits and common separators, length 5–30 after trim.</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class PhoneNumberAttribute : ValidationAttribute
{
    public PhoneNumberAttribute()
        : base("{0} must be a valid phone number (5–30 characters; digits and + - ( ) spaces).")
    {
    }

    public override bool IsValid(object? value)
    {
        if (value is null)
        {
            return true; // use [Required]/[NotBlank] for presence
        }

        if (value is not string raw)
        {
            return false;
        }

        var s = raw.Trim();
        if (s.Length is < 5 or > 30)
        {
            return false;
        }

        var digitCount = 0;
        foreach (var c in s)
        {
            if (char.IsDigit(c))
            {
                digitCount++;
                continue;
            }

            if (c is '+' or '-' or '(' or ')' or ' ')
            {
                continue;
            }

            return false;
        }

        return digitCount >= 5;
    }
}
