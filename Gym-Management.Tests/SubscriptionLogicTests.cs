using Gym_Management.Domain;
using Gym_Management.Services;
using Xunit;

namespace Gym_Management.Tests;

public class SubscriptionStatusTests
{
    private static readonly DateOnly Today = new(2026, 8, 26);

    private static Subscription Time(
        DateOnly start,
        DateOnly? end,
        DateTime? cancelled = null) => new()
    {
        Id = Guid.NewGuid(),
        CustomerId = Guid.NewGuid(),
        PlanId = Guid.NewGuid(),
        Type = SubscriptionType.Time,
        StartDate = start,
        EndDate = end,
        CancelledAtUtc = cancelled,
        CreatedAtUtc = DateTime.UtcNow,
        CreatedByStaffId = Guid.NewGuid()
    };

    private static Subscription Session(
        DateOnly start,
        int total,
        int used,
        DateTime? cancelled = null) => new()
    {
        Id = Guid.NewGuid(),
        CustomerId = Guid.NewGuid(),
        PlanId = Guid.NewGuid(),
        Type = SubscriptionType.Session,
        StartDate = start,
        TotalSessions = total,
        UsedSessions = used,
        CancelledAtUtc = cancelled,
        CreatedAtUtc = DateTime.UtcNow,
        CreatedByStaffId = Guid.NewGuid()
    };

    [Fact]
    public void Cancelled_Wins_Over_Everything()
    {
        var sub = Time(Today.AddDays(-10), Today.AddDays(10), cancelled: DateTime.UtcNow);
        Assert.Equal(DerivedSubscriptionStatus.Cancelled, SubscriptionStatus.Derive(sub, Today));
        Assert.False(SubscriptionStatus.IsNonTerminal(sub, Today));
    }

    [Fact]
    public void Scheduled_When_Start_In_Future()
    {
        var sub = Time(Today.AddDays(1), Today.AddDays(30));
        Assert.Equal(DerivedSubscriptionStatus.Scheduled, SubscriptionStatus.Derive(sub, Today));
        Assert.True(SubscriptionStatus.IsNonTerminal(sub, Today));
    }

    [Fact]
    public void Time_Active_Inclusive_EndDate()
    {
        var sub = Time(Today.AddDays(-5), Today);
        Assert.Equal(DerivedSubscriptionStatus.Active, SubscriptionStatus.Derive(sub, Today));
        Assert.True(SubscriptionStatus.IsNonTerminal(sub, Today));
    }

    [Fact]
    public void Time_Expired_When_Today_After_EndDate()
    {
        var sub = Time(Today.AddDays(-10), Today.AddDays(-1));
        Assert.Equal(DerivedSubscriptionStatus.Expired, SubscriptionStatus.Derive(sub, Today));
        Assert.False(SubscriptionStatus.IsNonTerminal(sub, Today));
    }

    [Fact]
    public void Session_Active_When_Remaining()
    {
        var sub = Session(Today.AddDays(-1), total: 10, used: 3);
        Assert.Equal(DerivedSubscriptionStatus.Active, SubscriptionStatus.Derive(sub, Today));
        Assert.True(SubscriptionStatus.IsNonTerminal(sub, Today));
    }

    [Fact]
    public void Session_Exhausted_When_Used_Reaches_Total()
    {
        var sub = Session(Today.AddDays(-1), total: 5, used: 5);
        Assert.Equal(DerivedSubscriptionStatus.Exhausted, SubscriptionStatus.Derive(sub, Today));
        Assert.False(SubscriptionStatus.IsNonTerminal(sub, Today));
    }

    [Fact]
    public void Session_Scheduled_Ignores_Sessions_Until_Start()
    {
        var sub = Session(Today.AddDays(2), total: 0, used: 0);
        Assert.Equal(DerivedSubscriptionStatus.Scheduled, SubscriptionStatus.Derive(sub, Today));
        Assert.True(SubscriptionStatus.IsNonTerminal(sub, Today));
    }

    [Fact]
    public void Session_Never_Expired_Even_With_Old_Start()
    {
        var sub = Session(Today.AddYears(-1), total: 2, used: 0);
        Assert.Equal(DerivedSubscriptionStatus.Active, SubscriptionStatus.Derive(sub, Today));
    }
}

public class SubscriptionRulesTests
{
    private static readonly DateOnly Today = new(2026, 8, 26);

    private static Subscription ActiveTime(DateOnly end) => new()
    {
        Id = Guid.NewGuid(),
        CustomerId = Guid.NewGuid(),
        PlanId = Guid.NewGuid(),
        Type = SubscriptionType.Time,
        StartDate = Today.AddDays(-10),
        EndDate = end,
        CreatedAtUtc = DateTime.UtcNow,
        CreatedByStaffId = Guid.NewGuid()
    };

    private static Subscription ActiveSession() => new()
    {
        Id = Guid.NewGuid(),
        CustomerId = Guid.NewGuid(),
        PlanId = Guid.NewGuid(),
        Type = SubscriptionType.Session,
        StartDate = Today.AddDays(-1),
        TotalSessions = 10,
        UsedSessions = 1,
        CreatedAtUtc = DateTime.UtcNow,
        CreatedByStaffId = Guid.NewGuid()
    };

    [Fact]
    public void Empty_Allows_Create()
    {
        Assert.Null(SubscriptionRules.ValidateNewSubscription([], Today, Today));
    }

    [Fact]
    public void Session_Blocks_Everything()
    {
        var msg = SubscriptionRules.ValidateNewSubscription([ActiveSession()], Today.AddDays(1), Today);
        Assert.NotNull(msg);
        Assert.Contains("session", msg, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Time_Blocks_Overlapping_Start()
    {
        var current = ActiveTime(Today.AddDays(10));
        var msg = SubscriptionRules.ValidateNewSubscription([current], Today.AddDays(5), Today);
        Assert.NotNull(msg);
    }

    [Fact]
    public void Time_Allows_Renewal_On_Day_After_End()
    {
        var end = Today.AddDays(10);
        var current = ActiveTime(end);
        Assert.Null(SubscriptionRules.ValidateNewSubscription([current], end.AddDays(1), Today));
    }

    [Fact]
    public void Time_Rejects_Renewal_On_End_Date()
    {
        var end = Today.AddDays(10);
        var current = ActiveTime(end);
        Assert.NotNull(SubscriptionRules.ValidateNewSubscription([current], end, Today));
    }

    [Fact]
    public void Two_NonTerminal_Time_Rejects()
    {
        var a = ActiveTime(Today.AddDays(10));
        var b = new Subscription
        {
            Id = Guid.NewGuid(),
            CustomerId = a.CustomerId,
            PlanId = Guid.NewGuid(),
            Type = SubscriptionType.Time,
            StartDate = Today.AddDays(11),
            EndDate = Today.AddDays(40),
            CreatedAtUtc = DateTime.UtcNow,
            CreatedByStaffId = Guid.NewGuid()
        };
        Assert.NotNull(SubscriptionRules.ValidateNewSubscription([a, b], Today.AddDays(41), Today));
    }

    [Fact]
    public void Expired_Time_Does_Not_Block()
    {
        var expired = ActiveTime(Today.AddDays(-1));
        Assert.Null(SubscriptionRules.ValidateNewSubscription([expired], Today, Today));
    }

    [Fact]
    public void Exhausted_Session_Does_Not_Block()
    {
        var exhausted = ActiveSession();
        exhausted.UsedSessions = exhausted.TotalSessions ?? 0;
        Assert.Null(SubscriptionRules.ValidateNewSubscription([exhausted], Today, Today));
    }

    [Fact]
    public void Cancelled_Does_Not_Block()
    {
        var cancelled = ActiveTime(Today.AddDays(30));
        cancelled.CancelledAtUtc = DateTime.UtcNow;
        Assert.Null(SubscriptionRules.ValidateNewSubscription([cancelled], Today, Today));
    }
}
