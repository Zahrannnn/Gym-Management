namespace Gym_Management.Observability;

/// <summary>Stable event ids for searchable structured logs.</summary>
public static class GymLogEvents
{
    public static readonly EventId RequestStarted = new(1000, nameof(RequestStarted));
    public static readonly EventId RequestCompleted = new(1001, nameof(RequestCompleted));
    public static readonly EventId RequestFailed = new(1002, nameof(RequestFailed));

    public static readonly EventId AuthLoginSucceeded = new(1100, nameof(AuthLoginSucceeded));
    public static readonly EventId AuthLoginFailed = new(1101, nameof(AuthLoginFailed));

    public static readonly EventId CheckInGranted = new(1200, nameof(CheckInGranted));
    public static readonly EventId CheckInDenied = new(1201, nameof(CheckInDenied));
    public static readonly EventId CheckInLockFailed = new(1202, nameof(CheckInLockFailed));

    public static readonly EventId ApiException = new(1300, nameof(ApiException));
    public static readonly EventId UnhandledException = new(1301, nameof(UnhandledException));

    public static readonly EventId StartupReady = new(1400, nameof(StartupReady));
}
