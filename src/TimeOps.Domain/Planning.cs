namespace TimeOps.Domain;

public enum TeamAccess { Member, Manager }
public enum PublicationState { Pending, Creating, Uncertain, IssueCreated, Published, Failed }

public sealed class AppUser
{
    public string Id { get; set; } = "";
    public string Login { get; set; } = "";
    public string Name { get; set; } = "";
    public bool IsAdmin { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class PlanningTeam
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string TimeZoneId { get; set; } = "America/Sao_Paulo";
    public int WorkingDaysMask { get; set; } = 62;
}

public sealed class TeamMembership
{
    public int Id { get; set; }
    public int TeamId { get; set; }
    public string UserId { get; set; } = "";
    public TeamAccess Access { get; set; }
}

public sealed class TeamRole
{
    public int Id { get; set; }
    public int TeamId { get; set; }
    public string Name { get; set; } = "";
}

public sealed class CapacityAllocation
{
    public int Id { get; set; }
    public int TeamId { get; set; }
    public string UserId { get; set; } = "";
    public int RoleId { get; set; }
    public int? SprintId { get; set; }
    public decimal HoursPerDay { get; set; }
}

public sealed class PlanningLeave
{
    public int Id { get; set; }
    public int TeamId { get; set; }
    public string? UserId { get; set; }
    public DateOnly Start { get; set; }
    public DateOnly End { get; set; }
}

public sealed class PlanningCycle
{
    public int Id { get; set; }
    public int TeamId { get; set; }
    public string Name { get; set; } = "";
}

public sealed class PlanningSprint
{
    public int Id { get; set; }
    public int TeamId { get; set; }
    public int CycleId { get; set; }
    public string Name { get; set; } = "";
    public DateOnly Start { get; set; }
    public DateOnly End { get; set; }
}

public sealed class PlanningProduct
{
    public int Id { get; set; }
    public int TeamId { get; set; }
    public string Name { get; set; } = "";
    public string Repository { get; set; } = "";
    public string RepositoryNodeId { get; set; } = "";
    public string ProjectNodeId { get; set; } = "";
    public int ProjectNumber { get; set; }
    public string ProjectUrl { get; set; } = "";
    public string StatusFieldId { get; set; } = "";
    public string StatusMappingJson { get; set; } = "{}";
}

public sealed class PlanningTask
{
    public int Id { get; set; }
    public int TeamId { get; set; }
    public Guid RequestId { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string CreatedBy { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class Implementation
{
    public int Id { get; set; }
    public Guid CorrelationId { get; set; } = Guid.NewGuid();
    public int TaskId { get; set; }
    public int ProductId { get; set; }
    public int? SprintId { get; set; }
    public decimal? Original { get; set; }
    public decimal? Remaining { get; set; }
    public int Version { get; set; }
    public string? IssueNodeId { get; set; }
    public int? IssueNumber { get; set; }
    public string? IssueUrl { get; set; }
    public string? ProjectItemId { get; set; }
    public string? GitHubTitle { get; set; }
    public string AssigneeIdsJson { get; set; } = "[]";
    public string StatusName { get; set; } = "Aguardando publicação";
    public string StateCategory { get; set; } = "Proposed";
    public bool Archived { get; set; }
    public PublicationState PublicationState { get; set; }
    public DateTimeOffset? SyncedAt { get; set; }
    public string? SyncError { get; set; }
}

public sealed class WorkLog
{
    public int Id { get; set; }
    public int ImplementationId { get; set; }
    public int SprintId { get; set; }
    public string UserId { get; set; } = "";
    public DateOnly Date { get; set; }
    public decimal Hours { get; set; }
    public bool Voided { get; set; }
    public int Version { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class PlanningEvent
{
    public int Id { get; set; }
    public int ImplementationId { get; set; }
    public string Kind { get; set; } = "";
    public int? FromSprintId { get; set; }
    public int? ToSprintId { get; set; }
    public decimal? Before { get; set; }
    public decimal? After { get; set; }
    public string ActorId { get; set; } = "";
    public DateTimeOffset At { get; set; }
}

public sealed class PublicationJob
{
    public int Id { get; set; }
    public int ImplementationId { get; set; }
    public int Attempts { get; set; }
    public DateTimeOffset NextAttemptAt { get; set; }
    public bool Completed { get; set; }
}

public sealed class WebhookDelivery
{
    public string Id { get; set; } = "";
    public string? IssueNodeId { get; set; }
    public string? ProjectItemId { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
    public bool Processed { get; set; }
}

public sealed class AuditEntry
{
    public int Id { get; set; }
    public string ActorId { get; set; } = "";
    public string Action { get; set; } = "";
    public string Entity { get; set; } = "";
    public string BeforeJson { get; set; } = "";
    public string AfterJson { get; set; } = "";
    public DateTimeOffset At { get; set; }
}

public sealed class PlanningData
{
    public List<AppUser> Users { get; set; } = [];
    public List<PlanningTeam> Teams { get; set; } = [];
    public List<TeamMembership> Memberships { get; set; } = [];
    public List<TeamRole> Roles { get; set; } = [];
    public List<CapacityAllocation> Capacities { get; set; } = [];
    public List<PlanningLeave> Leaves { get; set; } = [];
    public List<PlanningCycle> Cycles { get; set; } = [];
    public List<PlanningSprint> Sprints { get; set; } = [];
    public List<PlanningProduct> Products { get; set; } = [];
    public List<PlanningTask> Tasks { get; set; } = [];
    public List<Implementation> Implementations { get; set; } = [];
    public List<WorkLog> WorkLogs { get; set; } = [];
    public List<PlanningEvent> Events { get; set; } = [];
    public List<PublicationJob> Jobs { get; set; } = [];
    public List<WebhookDelivery> Deliveries { get; set; } = [];
    public List<AuditEntry> Audit { get; set; } = [];
}

public static class PlanningRules
{
    public static Result ValidateSprint(DateOnly start, DateOnly end, IEnumerable<PlanningSprint> others)
    {
        if (end < start) return Invalid("sprint.dates", "O fim da sprint deve ser igual ou posterior ao início.");
        if (others.Any(s => start <= s.End && end >= s.Start))
            return Invalid("sprint.overlap", "As sprints da mesma equipe não podem ter datas sobrepostas.");
        return Result.Success();
    }

    public static Result ValidateHours(decimal? hours, bool required = false)
        => hours is < 0 or > 100000 || required && hours is null
            ? Invalid("hours.invalid", "Informe horas entre zero e 100.000.") : Result.Success();

    public static Result Invalid(string code, string message) => Result.Failure(new(code, ErrorCategory.Validation, message));
}
