using DantesRoleplay.Applications;

namespace DantesRoleplay.TriggerScheduling.Tests;

public sealed class RecurringTriggerContractsTests
{
    private static readonly ApplicationIdentifier Application = ApplicationIdentifier.Parse("quest");

    [Fact]
    public void Closed_patterns_reject_invalid_shape_bounds_and_timezone()
    {
        AssertCode("RECURRENCE_INTERVAL", () => RecurrencePattern.Daily(0, new TimeOnly(12, 0), "Etc/UTC"));
        AssertCode("RECURRENCE_DATE_BOUNDS", () => RecurrencePattern.Daily(1, new TimeOnly(12, 0),
            "Etc/UTC", new DateOnly(2026, 2, 2), new DateOnly(2026, 2, 1)));
        AssertCode("RECURRENCE_WEEKDAYS", () => RecurrencePattern.Weekly(1, new TimeOnly(12, 0),
            "Etc/UTC", []));
        AssertCode("RECURRENCE_MONTH_DAY", () => RecurrencePattern.Monthly(1, new TimeOnly(12, 0),
            "Etc/UTC", 32));
        AssertCode("RECURRENCE_TIME_ZONE", () => RecurrencePattern.Daily(1, new TimeOnly(12, 0), "UTC"));
    }

    [Fact]
    public void Daily_interval_and_inclusive_bounds_are_calendar_based()
    {
        var trigger = Trigger(RecurrencePattern.Daily(2, new TimeOnly(10, 15), "Etc/UTC",
            new DateOnly(2026, 1, 2), new DateOnly(2026, 1, 6)));

        Assert.Equal(new DateTimeOffset(2026, 1, 2, 10, 15, 0, TimeSpan.Zero),
            RecurringScheduleEvaluator.NextOnOrAfter(trigger,
                new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero))!.OccurrenceAtUtc);
        Assert.Equal(new DateTimeOffset(2026, 1, 6, 10, 15, 0, TimeSpan.Zero),
            RecurringScheduleEvaluator.LatestOnOrBefore(trigger,
                new DateTimeOffset(2026, 1, 6, 23, 0, 0, TimeSpan.Zero))!.OccurrenceAtUtc);
        Assert.Null(RecurringScheduleEvaluator.NextAfter(trigger,
            new DateTimeOffset(2026, 1, 6, 10, 15, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void Weekly_sets_and_interval_use_a_fixed_monday_anchor()
    {
        var trigger = Trigger(RecurrencePattern.Weekly(2, new TimeOnly(9, 0), "Etc/UTC",
            [DayOfWeek.Monday, DayOfWeek.Friday], new DateOnly(2026, 1, 5)));

        Assert.Equal(new DateTimeOffset(2026, 1, 9, 9, 0, 0, TimeSpan.Zero),
            RecurringScheduleEvaluator.NextOnOrAfter(trigger,
                new DateTimeOffset(2026, 1, 6, 0, 0, 0, TimeSpan.Zero))!.OccurrenceAtUtc);
        Assert.Equal(new DateTimeOffset(2026, 1, 19, 9, 0, 0, TimeSpan.Zero),
            RecurringScheduleEvaluator.NextOnOrAfter(trigger,
                new DateTimeOffset(2026, 1, 10, 0, 0, 0, TimeSpan.Zero))!.OccurrenceAtUtc);
    }

    [Theory]
    [InlineData(28, "2026-02-28T08:00:00+00:00")]
    [InlineData(29, "2028-02-29T08:00:00+00:00")]
    [InlineData(30, "2026-03-30T08:00:00+00:00")]
    [InlineData(31, "2026-03-31T08:00:00+00:00")]
    public void Monthly_days_skip_months_where_the_day_does_not_exist(int day, string expected)
    {
        var start = day == 29 ? new DateOnly(2028, 2, 1) : new DateOnly(2026, 2, 1);
        var trigger = Trigger(RecurrencePattern.Monthly(1, new TimeOnly(8, 0), "Etc/UTC", day, start));
        Assert.Equal(DateTimeOffset.Parse(expected), RecurringScheduleEvaluator.NextOnOrAfter(trigger,
            new DateTimeOffset(start.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero))!.OccurrenceAtUtc);
    }

    [Fact]
    public void Stockholm_spring_gap_honors_skip_and_next_valid()
    {
        var skip = Trigger(RecurrencePattern.Daily(1, new TimeOnly(2, 30), "Europe/Stockholm",
            new DateOnly(2026, 3, 29), gapPolicy: RecurrenceGapPolicy.Skip));
        var nextValid = Trigger(RecurrencePattern.Daily(1, new TimeOnly(2, 30), "Europe/Stockholm",
            new DateOnly(2026, 3, 29), gapPolicy: RecurrenceGapPolicy.NextValid));
        var reference = new DateTimeOffset(2026, 3, 28, 0, 0, 0, TimeSpan.Zero);

        Assert.Equal(new DateTimeOffset(2026, 3, 30, 0, 30, 0, TimeSpan.Zero),
            RecurringScheduleEvaluator.NextOnOrAfter(skip, reference)!.OccurrenceAtUtc);
        Assert.Equal(new DateTimeOffset(2026, 3, 29, 1, 0, 0, TimeSpan.Zero),
            RecurringScheduleEvaluator.NextOnOrAfter(nextValid, reference)!.OccurrenceAtUtc);
    }

    [Fact]
    public void Stockholm_autumn_overlap_honors_earlier_and_later()
    {
        var earlier = Trigger(RecurrencePattern.Daily(1, new TimeOnly(2, 30), "Europe/Stockholm",
            new DateOnly(2026, 10, 25), overlapPolicy: RecurrenceOverlapPolicy.Earlier));
        var later = Trigger(RecurrencePattern.Daily(1, new TimeOnly(2, 30), "Europe/Stockholm",
            new DateOnly(2026, 10, 25), overlapPolicy: RecurrenceOverlapPolicy.Later));
        var reference = new DateTimeOffset(2026, 10, 24, 0, 0, 0, TimeSpan.Zero);

        Assert.Equal(new DateTimeOffset(2026, 10, 25, 0, 30, 0, TimeSpan.Zero),
            RecurringScheduleEvaluator.NextOnOrAfter(earlier, reference)!.OccurrenceAtUtc);
        Assert.Equal(new DateTimeOffset(2026, 10, 25, 1, 30, 0, TimeSpan.Zero),
            RecurringScheduleEvaluator.NextOnOrAfter(later, reference)!.OccurrenceAtUtc);
    }

    [Fact]
    public void Occurrence_fire_identity_is_deterministic_and_version_scoped()
    {
        var occurrence = new DateTimeOffset(2026, 8, 25, 20, 0, 0, TimeSpan.Zero);
        var first = Trigger(RecurrencePattern.Daily(1, new TimeOnly(20, 0), "Etc/UTC"));
        var second = RecurringTriggerDefinition.Create(Application, first.Id, 2, first.Pattern);

        Assert.Equal(TriggerSchedulingFingerprint.RecurringFire(first, occurrence),
            TriggerSchedulingFingerprint.RecurringFire(first, occurrence));
        Assert.NotEqual(TriggerSchedulingFingerprint.RecurringFire(first, occurrence),
            TriggerSchedulingFingerprint.RecurringFire(second, occurrence));
    }

    [Fact]
    public void Forever_impossible_aligned_month_returns_no_occurrence_at_calendar_end()
    {
        var trigger = Trigger(RecurrencePattern.Monthly(12, new TimeOnly(8, 0), "Etc/UTC", 31,
            new DateOnly(2026, 2, 1)));

        Assert.Null(RecurringScheduleEvaluator.NextOnOrAfter(trigger,
            new DateTimeOffset(9999, 1, 1, 0, 0, 0, TimeSpan.Zero)));
    }

    private static RecurringTriggerDefinition Trigger(RecurrencePattern pattern) =>
        RecurringTriggerDefinition.Create(Application, "trigger.recurring.contract", 1, pattern);

    private static void AssertCode(string code, Action action) =>
        Assert.Equal(code, Assert.Throws<TriggerSchedulingContractException>(action).Code);
}
