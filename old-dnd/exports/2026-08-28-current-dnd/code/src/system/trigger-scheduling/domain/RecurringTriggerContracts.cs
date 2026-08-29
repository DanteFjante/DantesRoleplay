using System.Text.RegularExpressions;
using DantesRoleplay.Applications;

namespace DantesRoleplay.TriggerScheduling;

public enum RecurrenceKind { Daily, Weekly, Monthly }
public enum RecurrenceGapPolicy { Skip, NextValid }
public enum RecurrenceOverlapPolicy { Earlier, Later }
public enum RecurringTriggerLifecycle { Active, Paused, Cancelled }

public sealed record RecurrencePattern
{
    private static readonly DateOnly Epoch = new(1970, 1, 1);
    private static readonly Regex IanaZone = new(
        "^(?:Etc/[A-Za-z0-9_+.-]+|[A-Za-z_]+(?:/[A-Za-z0-9_+.-]+)+)$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    private RecurrencePattern(
        RecurrenceKind kind,
        int interval,
        TimeOnly localTime,
        string timeZoneId,
        DateOnly? startDate,
        DateOnly? endDate,
        IReadOnlyList<DayOfWeek> weekdays,
        int? dayOfMonth,
        RecurrenceGapPolicy gapPolicy,
        RecurrenceOverlapPolicy overlapPolicy)
    {
        if (!Enum.IsDefined(kind)) throw Failure("RECURRENCE_KIND", "The recurrence kind is invalid.");
        if (interval is < 1 or > 365) throw Failure("RECURRENCE_INTERVAL", "The recurrence interval must be from 1 through 365.");
        if (localTime.Ticks % TimeSpan.TicksPerSecond != 0)
            throw Failure("RECURRENCE_LOCAL_TIME", "The recurrence local time must use whole seconds.");
        if (endDate is not null && startDate is not null && endDate < startDate)
            throw Failure("RECURRENCE_DATE_BOUNDS", "The recurrence end date cannot precede its start date.");
        if (!Enum.IsDefined(gapPolicy)) throw Failure("RECURRENCE_GAP_POLICY", "The DST gap policy is invalid.");
        if (!Enum.IsDefined(overlapPolicy)) throw Failure("RECURRENCE_OVERLAP_POLICY", "The DST overlap policy is invalid.");
        TimeZoneId = RequireIanaZone(timeZoneId);
        if (weekdays is null || weekdays.Any(value => !Enum.IsDefined(value)) ||
            weekdays.Distinct().Count() != weekdays.Count)
            throw Failure("RECURRENCE_WEEKDAYS", "The recurrence weekdays are invalid or repeated.");
        if (kind == RecurrenceKind.Weekly && weekdays.Count == 0 ||
            kind != RecurrenceKind.Weekly && weekdays.Count != 0)
            throw Failure("RECURRENCE_WEEKDAYS", "Only weekly recurrence declares one or more weekdays.");
        if (kind == RecurrenceKind.Monthly && dayOfMonth is not (>= 1 and <= 31) ||
            kind != RecurrenceKind.Monthly && dayOfMonth is not null)
            throw Failure("RECURRENCE_MONTH_DAY", "Only monthly recurrence declares a day from 1 through 31.");

        Kind = kind;
        Interval = interval;
        LocalTime = localTime;
        StartDate = startDate;
        EndDate = endDate;
        Weekdays = Array.AsReadOnly(weekdays.OrderBy(MondayIndex).ToArray());
        DayOfMonth = dayOfMonth;
        GapPolicy = gapPolicy;
        OverlapPolicy = overlapPolicy;
    }

    public RecurrenceKind Kind { get; }
    public int Interval { get; }
    public TimeOnly LocalTime { get; }
    public string TimeZoneId { get; }
    public DateOnly? StartDate { get; }
    public DateOnly? EndDate { get; }
    public IReadOnlyList<DayOfWeek> Weekdays { get; }
    public int? DayOfMonth { get; }
    public RecurrenceGapPolicy GapPolicy { get; }
    public RecurrenceOverlapPolicy OverlapPolicy { get; }
    internal DateOnly AnchorDate => StartDate ?? Epoch;

    public static RecurrencePattern Daily(
        int interval,
        TimeOnly localTime,
        string timeZoneId,
        DateOnly? startDate = null,
        DateOnly? endDate = null,
        RecurrenceGapPolicy gapPolicy = RecurrenceGapPolicy.Skip,
        RecurrenceOverlapPolicy overlapPolicy = RecurrenceOverlapPolicy.Earlier) =>
        new(RecurrenceKind.Daily, interval, localTime, timeZoneId, startDate, endDate, [], null,
            gapPolicy, overlapPolicy);

    public static RecurrencePattern Weekly(
        int interval,
        TimeOnly localTime,
        string timeZoneId,
        IReadOnlyList<DayOfWeek> weekdays,
        DateOnly? startDate = null,
        DateOnly? endDate = null,
        RecurrenceGapPolicy gapPolicy = RecurrenceGapPolicy.Skip,
        RecurrenceOverlapPolicy overlapPolicy = RecurrenceOverlapPolicy.Earlier) =>
        new(RecurrenceKind.Weekly, interval, localTime, timeZoneId, startDate, endDate,
            weekdays, null, gapPolicy, overlapPolicy);

    public static RecurrencePattern Monthly(
        int interval,
        TimeOnly localTime,
        string timeZoneId,
        int dayOfMonth,
        DateOnly? startDate = null,
        DateOnly? endDate = null,
        RecurrenceGapPolicy gapPolicy = RecurrenceGapPolicy.Skip,
        RecurrenceOverlapPolicy overlapPolicy = RecurrenceOverlapPolicy.Earlier) =>
        new(RecurrenceKind.Monthly, interval, localTime, timeZoneId, startDate, endDate, [],
            dayOfMonth, gapPolicy, overlapPolicy);

    internal static RecurrencePattern Rehydrate(
        RecurrenceKind kind, int interval, TimeOnly localTime, string timeZoneId,
        DateOnly? startDate, DateOnly? endDate, IReadOnlyList<DayOfWeek> weekdays,
        int? dayOfMonth, RecurrenceGapPolicy gapPolicy, RecurrenceOverlapPolicy overlapPolicy) =>
        new(kind, interval, localTime, timeZoneId, startDate, endDate, weekdays, dayOfMonth,
            gapPolicy, overlapPolicy);

    internal TimeZoneInfo Zone() => TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId);
    internal static int MondayIndex(DayOfWeek value) => ((int)value + 6) % 7;

    private static string RequireIanaZone(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 100 || !IanaZone.IsMatch(value))
            throw Failure("RECURRENCE_TIME_ZONE", "The recurrence requires a bounded IANA timezone identifier.");
        try { _ = TimeZoneInfo.FindSystemTimeZoneById(value); }
        catch (TimeZoneNotFoundException) { throw Failure("RECURRENCE_TIME_ZONE", "The recurrence timezone is unavailable on this host."); }
        catch (InvalidTimeZoneException) { throw Failure("RECURRENCE_TIME_ZONE", "The recurrence timezone data is invalid on this host."); }
        return value;
    }

    private static TriggerSchedulingContractException Failure(string code, string message) => new(code, message);
}

public sealed record RecurringTriggerDefinition
{
    private RecurringTriggerDefinition(
        ApplicationIdentifier applicationId,
        string id,
        int version,
        RecurringTriggerLifecycle lifecycle,
        RecurrencePattern pattern,
        TriggerMisfirePolicy misfirePolicy,
        TriggerFireTarget target,
        TriggerNotificationTarget? notification)
    {
        ApplicationId = applicationId ?? throw new ArgumentNullException(nameof(applicationId));
        Id = OneTimeTriggerDefinition.Create(applicationId, id, 1, DateTimeOffset.UnixEpoch,
            TriggerMisfirePolicy.FireOnce).Id;
        if (version < 1) throw new TriggerSchedulingContractException("INVALID_TRIGGER_VERSION", "The trigger version must be positive.");
        if (!Enum.IsDefined(lifecycle)) throw new TriggerSchedulingContractException("RECURRING_TRIGGER_LIFECYCLE", "The recurring trigger lifecycle is invalid.");
        Pattern = pattern ?? throw new ArgumentNullException(nameof(pattern));
        if (!Enum.IsDefined(misfirePolicy)) throw new TriggerSchedulingContractException("TRIGGER_MISFIRE_POLICY", "The trigger misfire policy is invalid.");
        if (target != TriggerFireTarget.NotificationOnly) throw new TriggerSchedulingContractException("TRIGGER_TARGET_UNSUPPORTED", "Only notification-only triggers are supported.");
        Version = version;
        Lifecycle = lifecycle;
        MisfirePolicy = misfirePolicy;
        Target = target;
        Notification = notification ?? TriggerNotificationTarget.Default(Id);
    }

    public ApplicationIdentifier ApplicationId { get; }
    public string Id { get; }
    public int Version { get; }
    public RecurringTriggerLifecycle Lifecycle { get; }
    public RecurrencePattern Pattern { get; }
    public TriggerMisfirePolicy MisfirePolicy { get; }
    public TriggerFireTarget Target { get; }
    public TriggerNotificationTarget Notification { get; }

    public static RecurringTriggerDefinition Create(
        ApplicationIdentifier applicationId,
        string id,
        int version,
        RecurrencePattern pattern,
        RecurringTriggerLifecycle lifecycle = RecurringTriggerLifecycle.Active,
        TriggerMisfirePolicy misfirePolicy = TriggerMisfirePolicy.Skip,
        TriggerFireTarget target = TriggerFireTarget.NotificationOnly,
        TriggerNotificationTarget? notification = null) =>
        new(applicationId, id, version, lifecycle, pattern, misfirePolicy, target, notification);
}

public sealed record RecurringOccurrence(
    DateOnly ScheduledLocalDate,
    DateTime ResolvedLocalDateTime,
    DateTimeOffset OccurrenceAtUtc);

public static class RecurringScheduleEvaluator
{
    public static RecurringOccurrence? NextOnOrAfter(
        RecurringTriggerDefinition definition,
        DateTimeOffset referenceUtc) => Find(definition, referenceUtc, forward: true, inclusive: true);

    public static RecurringOccurrence? NextAfter(
        RecurringTriggerDefinition definition,
        DateTimeOffset occurrenceUtc) => Find(definition, occurrenceUtc, forward: true, inclusive: false);

    public static RecurringOccurrence? LatestOnOrBefore(
        RecurringTriggerDefinition definition,
        DateTimeOffset referenceUtc) => Find(definition, referenceUtc, forward: false, inclusive: true);

    private static RecurringOccurrence? Find(
        RecurringTriggerDefinition definition,
        DateTimeOffset referenceUtc,
        bool forward,
        bool inclusive)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (referenceUtc.Offset != TimeSpan.Zero)
            throw new TriggerSchedulingContractException("TRIGGER_CLOCK_NOT_UTC", "The trigger clock must use UTC.");
        var zone = definition.Pattern.Zone();
        var localReference = TimeZoneInfo.ConvertTime(referenceUtc, zone).DateTime;
        var searchDate = DateOnly.FromDateTime(localReference);
        for (var attempts = 0; attempts < 5000; attempts++)
        {
            var date = forward
                ? NextCandidateDate(definition.Pattern, searchDate)
                : PreviousCandidateDate(definition.Pattern, searchDate);
            if (date is null) return null;
            var occurrence = Resolve(definition.Pattern, date.Value, zone);
            if (occurrence is not null)
            {
                var comparison = occurrence.OccurrenceAtUtc.CompareTo(referenceUtc);
                if (forward && (comparison > 0 || inclusive && comparison == 0) ||
                    !forward && (comparison < 0 || inclusive && comparison == 0))
                    return occurrence;
            }
            searchDate = forward ? date.Value.AddDays(1) : date.Value.AddDays(-1);
        }
        throw new TriggerSchedulingContractException("RECURRENCE_SEARCH_BOUND", "The recurrence could not resolve within the Gregorian search bound.");
    }

    private static RecurringOccurrence? Resolve(
        RecurrencePattern pattern,
        DateOnly date,
        TimeZoneInfo zone)
    {
        var local = DateTime.SpecifyKind(date.ToDateTime(pattern.LocalTime), DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(local))
        {
            if (pattern.GapPolicy == RecurrenceGapPolicy.Skip) return null;
            local = local.AddTicks(-(local.Ticks % TimeSpan.TicksPerMinute));
            for (var minute = 0; minute <= 2880 && zone.IsInvalidTime(local); minute++)
                local = local.AddMinutes(1);
            if (zone.IsInvalidTime(local))
                throw new TriggerSchedulingContractException("RECURRENCE_DST_GAP", "The DST gap has no bounded next valid local time.");
        }
        TimeSpan offset;
        if (zone.IsAmbiguousTime(local))
        {
            var offsets = zone.GetAmbiguousTimeOffsets(local);
            offset = pattern.OverlapPolicy == RecurrenceOverlapPolicy.Earlier
                ? offsets.Max()
                : offsets.Min();
        }
        else
        {
            offset = zone.GetUtcOffset(local);
        }
        return new(date, local, new DateTimeOffset(local, offset).ToUniversalTime());
    }

    private static DateOnly? NextCandidateDate(RecurrencePattern pattern, DateOnly target)
    {
        if (pattern.StartDate is { } start && target < start) target = start;
        if (pattern.EndDate is { } end && target > end) return null;
        DateOnly? result = pattern.Kind switch
        {
            RecurrenceKind.Daily => NextDaily(pattern, target),
            RecurrenceKind.Weekly => NextWeekly(pattern, target),
            RecurrenceKind.Monthly => NextMonthly(pattern, target),
            _ => null
        };
        return result is not null && pattern.EndDate is { } maximum && result > maximum ? null : result;
    }

    private static DateOnly? PreviousCandidateDate(RecurrencePattern pattern, DateOnly target)
    {
        if (pattern.EndDate is { } end && target > end) target = end;
        if (pattern.StartDate is { } start && target < start) return null;
        DateOnly? result = pattern.Kind switch
        {
            RecurrenceKind.Daily => PreviousDaily(pattern, target),
            RecurrenceKind.Weekly => PreviousWeekly(pattern, target),
            RecurrenceKind.Monthly => PreviousMonthly(pattern, target),
            _ => null
        };
        return result is not null && pattern.StartDate is { } minimum && result < minimum ? null : result;
    }

    private static DateOnly NextDaily(RecurrencePattern pattern, DateOnly target)
    {
        var anchor = pattern.AnchorDate;
        if (target <= anchor) return anchor;
        var days = target.DayNumber - anchor.DayNumber;
        return anchor.AddDays(((days + pattern.Interval - 1) / pattern.Interval) * pattern.Interval);
    }

    private static DateOnly? PreviousDaily(RecurrencePattern pattern, DateOnly target)
    {
        var anchor = pattern.AnchorDate;
        if (target < anchor) return null;
        var days = target.DayNumber - anchor.DayNumber;
        return anchor.AddDays(days / pattern.Interval * pattern.Interval);
    }

    private static DateOnly NextWeekly(RecurrencePattern pattern, DateOnly target)
    {
        var anchorWeek = WeekStart(pattern.AnchorDate);
        var targetWeek = WeekStart(target);
        var weeks = Math.Max(0, (targetWeek.DayNumber - anchorWeek.DayNumber) / 7);
        var block = (weeks + pattern.Interval - 1) / pattern.Interval;
        if (weeks % pattern.Interval == 0) block = weeks / pattern.Interval;
        for (;; block++)
        {
            var week = anchorWeek.AddDays(block * pattern.Interval * 7);
            foreach (var weekday in pattern.Weekdays)
            {
                var candidate = week.AddDays(RecurrencePattern.MondayIndex(weekday));
                if (candidate >= pattern.AnchorDate && candidate >= target) return candidate;
            }
        }
    }

    private static DateOnly? PreviousWeekly(RecurrencePattern pattern, DateOnly target)
    {
        var anchorWeek = WeekStart(pattern.AnchorDate);
        var targetWeek = WeekStart(target);
        var weeks = (targetWeek.DayNumber - anchorWeek.DayNumber) / 7;
        if (weeks < 0) return null;
        var block = weeks / pattern.Interval;
        for (; block >= 0; block--)
        {
            var week = anchorWeek.AddDays(block * pattern.Interval * 7);
            foreach (var weekday in pattern.Weekdays.Reverse())
            {
                var candidate = week.AddDays(RecurrencePattern.MondayIndex(weekday));
                if (candidate >= pattern.AnchorDate && candidate <= target) return candidate;
            }
        }
        return null;
    }

    private static DateOnly? NextMonthly(RecurrencePattern pattern, DateOnly target)
    {
        var anchorIndex = MonthIndex(pattern.AnchorDate);
        var targetIndex = MonthIndex(target);
        var diff = Math.Max(0, targetIndex - anchorIndex);
        var block = (diff + pattern.Interval - 1) / pattern.Interval;
        if (diff % pattern.Interval == 0) block = diff / pattern.Interval;
        for (var attempt = 0; attempt < 4800; attempt++, block++)
        {
            var month = Month(anchorIndex + block * pattern.Interval);
            if (month is null) return null;
            if (pattern.EndDate is { } end && month > new DateOnly(end.Year, end.Month, 1)) return null;
            if (pattern.DayOfMonth <= DateTime.DaysInMonth(month.Value.Year, month.Value.Month))
            {
                var candidate = new DateOnly(month.Value.Year, month.Value.Month, pattern.DayOfMonth!.Value);
                if (candidate >= pattern.AnchorDate && candidate >= target) return candidate;
            }
        }
        return null;
    }

    private static DateOnly? PreviousMonthly(RecurrencePattern pattern, DateOnly target)
    {
        var anchorIndex = MonthIndex(pattern.AnchorDate);
        var targetIndex = MonthIndex(target);
        var diff = targetIndex - anchorIndex;
        if (diff < 0) return null;
        var block = diff / pattern.Interval;
        for (var attempt = 0; attempt < 4800 && block >= 0; attempt++, block--)
        {
            var month = Month(anchorIndex + block * pattern.Interval);
            if (month is null) return null;
            if (pattern.DayOfMonth <= DateTime.DaysInMonth(month.Value.Year, month.Value.Month))
            {
                var candidate = new DateOnly(month.Value.Year, month.Value.Month, pattern.DayOfMonth!.Value);
                if (candidate >= pattern.AnchorDate && candidate <= target) return candidate;
            }
        }
        return null;
    }

    private static DateOnly WeekStart(DateOnly date) =>
        date.AddDays(-RecurrencePattern.MondayIndex(date.DayOfWeek));
    private static int MonthIndex(DateOnly date) => checked(date.Year * 12 + date.Month - 1);
    private static DateOnly? Month(int index) => index is >= 12 and <= 119999
        ? new DateOnly(index / 12, index % 12 + 1, 1)
        : null;
}
