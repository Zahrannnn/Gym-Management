namespace Gym_Management.Validation;

/// <summary>
/// Machine-readable <c>reason</c> values returned in ProblemDetails (and check-in denial payload).
/// Keep in sync with README "Error contract".
/// </summary>
public static class ErrorReasons
{
    // Transport / auth
    public const string Unauthorized = "unauthorized";
    public const string Forbidden = "forbidden";
    public const string NotFound = "not_found";
    public const string Validation = "validation";
    public const string OverlapConflict = "overlap_conflict";
    public const string RateLimited = "rate_limited";
    public const string InternalError = "internal_error";

    // Check-in domain denials (HTTP 200 + result=denied)
    public const string TokenUnknown = "token_unknown";
    public const string DuplicateScan = "duplicate_scan";
    public const string NotStarted = "not_started";
    public const string Expired = "expired";
    public const string NoSessionsRemaining = "no_sessions_remaining";
    public const string NoActiveSubscription = "no_active_subscription";
    public const string CustomerInactive = "customer_inactive";
}
