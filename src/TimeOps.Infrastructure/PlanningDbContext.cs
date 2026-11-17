using Microsoft.EntityFrameworkCore;
using TimeOps.Domain;

namespace TimeOps.Infrastructure;

public sealed class PlanningDbContext(DbContextOptions<PlanningDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<AppUser>().HasKey(x => x.Id);
        b.Entity<WebhookDelivery>().HasKey(x => x.Id);
        b.Entity<AppUser>().Property(x => x.Id).HasMaxLength(100);
        b.Entity<WebhookDelivery>().Property(x => x.Id).HasMaxLength(100);
        Entity<PlanningTeam>(b); Entity<TeamMembership>(b); Entity<TeamRole>(b); Entity<CapacityAllocation>(b);
        Entity<PlanningLeave>(b); Entity<PlanningCycle>(b); Entity<PlanningSprint>(b); Entity<PlanningProduct>(b);
        Entity<PlanningTask>(b); Entity<Implementation>(b); Entity<WorkLog>(b); Entity<PlanningEvent>(b);
        Entity<PublicationJob>(b); Entity<AuditEntry>(b);
        b.Entity<TeamMembership>().HasIndex(x => new { x.TeamId, x.UserId }).IsUnique();
        b.Entity<TeamRole>().HasIndex(x => new { x.TeamId, x.Name }).IsUnique();
        b.Entity<PlanningTask>().HasIndex(x => x.RequestId).IsUnique();
        b.Entity<Implementation>().HasIndex(x => new { x.TaskId, x.ProductId }).IsUnique();
        b.Entity<Implementation>().HasIndex(x => x.CorrelationId).IsUnique();
        b.Entity<Implementation>().HasIndex(x => x.IssueNodeId).IsUnique();
        b.Entity<PublicationJob>().HasIndex(x => x.ImplementationId).IsUnique();
        b.Entity<PublicationJob>().HasIndex(x => new { x.Completed, x.NextAttemptAt });
        b.Entity<WorkLog>().HasIndex(x => new { x.SprintId, x.UserId, x.Date });
        b.Entity<PlanningEvent>().HasIndex(x => new { x.ImplementationId, x.At });
        b.Entity<CapacityAllocation>().HasIndex(x => new { x.TeamId, x.UserId, x.RoleId, x.SprintId }).IsUnique();
        Reference<TeamMembership, PlanningTeam>(b, "TeamId"); Reference<TeamMembership, AppUser>(b, "UserId");
        Reference<TeamRole, PlanningTeam>(b, "TeamId"); Reference<CapacityAllocation, PlanningTeam>(b, "TeamId");
        Reference<CapacityAllocation, AppUser>(b, "UserId"); Reference<CapacityAllocation, TeamRole>(b, "RoleId");
        Reference<CapacityAllocation, PlanningSprint>(b, "SprintId"); Reference<PlanningLeave, PlanningTeam>(b, "TeamId");
        Reference<PlanningLeave, AppUser>(b, "UserId"); Reference<PlanningCycle, PlanningTeam>(b, "TeamId");
        Reference<PlanningSprint, PlanningCycle>(b, "CycleId"); Reference<PlanningSprint, PlanningTeam>(b, "TeamId");
        Reference<PlanningProduct, PlanningTeam>(b, "TeamId"); Reference<PlanningTask, PlanningTeam>(b, "TeamId");
        Reference<PlanningTask, AppUser>(b, "CreatedBy"); Reference<Implementation, PlanningTask>(b, "TaskId");
        Reference<Implementation, PlanningProduct>(b, "ProductId"); Reference<Implementation, PlanningSprint>(b, "SprintId");
        Reference<WorkLog, Implementation>(b, "ImplementationId"); Reference<WorkLog, PlanningSprint>(b, "SprintId");
        Reference<WorkLog, AppUser>(b, "UserId"); Reference<PlanningEvent, Implementation>(b, "ImplementationId");
        Reference<PublicationJob, Implementation>(b, "ImplementationId");
        foreach (var type in b.Model.GetEntityTypes())
            foreach (var p in type.GetProperties().Where(p => p.ClrType == typeof(decimal) || p.ClrType == typeof(decimal?)))
            { p.SetPrecision(18); p.SetScale(4); }
    }

    private static void Entity<T>(ModelBuilder b) where T : class
    { b.Entity<T>().HasKey("Id"); b.Entity<T>().Property<int>("Id").ValueGeneratedNever(); }
    private static void Reference<T, TParent>(ModelBuilder b, string key) where T : class where TParent : class
        => b.Entity<T>().HasOne<TParent>().WithMany().HasForeignKey(key).OnDelete(DeleteBehavior.Restrict);
}
