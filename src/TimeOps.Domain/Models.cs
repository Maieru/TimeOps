namespace TimeOps.Domain;

public sealed record NamedItem(string Id, string Name);
public sealed record Person(string Id, string Name);
public sealed record DayRange(DateOnly Start, DateOnly End)
{
    public bool Contains(DateOnly day) => day >= Start && day <= End;
}

public sealed record Sprint(string Id, string Name, string Path, DateOnly? Start, DateOnly? End);
public sealed record MemberCapacity(Person Person, IReadOnlyList<decimal> ActivitiesPerDay, IReadOnlyList<DayRange> DaysOff);
public sealed record TeamCalendar(IReadOnlySet<DayOfWeek> WorkingDays, IReadOnlyList<DayRange> DaysOff);
public sealed record EffortFields(bool Completed, bool Original, bool Remaining);
public sealed record TaskWork(
    int Id, string Title, Person? Assignee, string State, string StateCategory,
    string AreaPath, string IterationPath, decimal? Completed, decimal? Original,
    decimal? Remaining, string Url, int? ParentId = null, int? Revision = null, DateTimeOffset? ChangedAt = null);

public sealed record ParentWorkItem(int Id, string Title, string Type, int? ParentId, string Url);
public sealed record StoryEffort(ParentWorkItem? Story, IReadOnlyList<TaskWork> Tasks, decimal? Completed);
public sealed record FeatureEffort(ParentWorkItem? Feature, IReadOnlyList<StoryEffort> Stories, decimal? Completed);

public sealed record SprintSnapshot(
    Sprint Sprint, TeamCalendar Calendar, IReadOnlyList<MemberCapacity> Capacities,
    IReadOnlyList<TaskWork> Tasks, EffortFields Fields, DateTimeOffset CollectedAt,
    IReadOnlyList<ParentWorkItem>? Parents = null, string? HierarchyError = null);

public sealed record DataWarning(string Code, string Message);

public sealed record PersonMetrics(
    Person? Person, decimal? DailyCapacity, int ElapsedDays, int TotalDays, int FutureDays,
    decimal? Expected, decimal? TotalCapacity, decimal? FutureCapacity,
    decimal? Completed, decimal? Original, decimal? Remaining,
    decimal? Difference, decimal? Coverage, decimal? FutureBalance,
    IReadOnlyList<TaskWork> Tasks, IReadOnlyList<DataWarning> Warnings);

public sealed record TeamMetrics(
    decimal? Expected, decimal? TotalCapacity, decimal? FutureCapacity,
    decimal? Completed, decimal? Original, decimal? Remaining,
    decimal? Difference, decimal? Coverage, decimal? FutureBalance,
    int TaskCount, int CompletedTaskCount, decimal? TaskProgress,
    IReadOnlyDictionary<string, int> StateCategories);

public sealed record Dashboard(
    Sprint Sprint, DateOnly ReferenceDate, DateTimeOffset CollectedAt,
    IReadOnlyList<PersonMetrics> People, PersonMetrics? Unassigned,
    TeamMetrics Team, IReadOnlyList<DataWarning> Warnings,
    IReadOnlyList<FeatureEffort>? Features = null, string? HierarchyError = null);

public enum EffortField { Completed, Original, Remaining }

public sealed record EffortChange(
    int TaskId, string TaskTitle, string TaskUrl, int UpdateId,
    DateTimeOffset ChangedAt, Person? ChangedBy, EffortField Field,
    decimal? Before, decimal? After)
{
    public decimal Delta => (After ?? 0) - (Before ?? 0);
}

public sealed record EffortHistory(
    DateTimeOffset From, DateTimeOffset To, DateTimeOffset CollectedAt,
    IReadOnlyList<EffortChange> Changes);
