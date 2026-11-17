using TimeOps.Domain;

namespace TimeOps.Application;

// Each mutation is an atomic transaction. Callbacks must contain no external I/O.
public interface IPlanningStore
{
    Task<Result<T>> ReadAsync<T>(Func<PlanningData, Result<T>> query, CancellationToken ct = default);
    Task<Result<T>> ChangeAsync<T>(Func<PlanningData, Result<T>> command, CancellationToken ct = default);
}

public interface ICurrentActor { string UserId { get; } }
public sealed record SprintInput(string Name, DateOnly Start, DateOnly End);
public sealed record ImplementationInput(int ProductId, int? SprintId, decimal? Original, decimal? Remaining);
public sealed record GitHubProjectInfo(string RepositoryId, string ProjectId, string ProjectUrl,
    string StatusFieldId, IReadOnlyDictionary<string, string> StatusOptions);
public sealed record GitHubIssue(string NodeId, int Number, string Url, string Title, IReadOnlyList<string> AssigneeIds);
public sealed record GitHubExecution(GitHubIssue Issue, string? ItemId, string? StatusOptionId, string? StatusName, bool Archived);

public interface IGitHubProjectsGateway
{
    Task<Result<GitHubProjectInfo>> InspectAsync(string repository, int projectNumber, CancellationToken ct);
    Task<Result<AppUser>> FindUserAsync(string login, CancellationToken ct);
    Task<Result<GitHubIssue?>> FindIssueAsync(string repository, Guid correlationId, CancellationToken ct);
    Task<Result<GitHubIssue>> CreateIssueAsync(string repository, string title, string body, Guid correlationId, CancellationToken ct);
    Task<Result<string>> AddToProjectAsync(string projectId, string issueId, CancellationToken ct);
    Task<Result<GitHubExecution>> ReadExecutionAsync(PlanningProduct product, string issueId, CancellationToken ct);
}

public sealed record PlanningWorkspace(AppUser User, IReadOnlyList<PlanningTeam> Teams, PlanningData Data);
public sealed record PlanningHistoryRow(DateTimeOffset At, string Actor, string Task, string Kind, decimal? Before, decimal? After);
