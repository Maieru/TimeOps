using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TimeOps.Application;
using TimeOps.Domain;

namespace TimeOps.Infrastructure;

public sealed class EfPlanningStore(IDbContextFactory<PlanningDbContext> factory, ILogger<EfPlanningStore> logger) : IPlanningStore
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    public Task<Result<T>> ReadAsync<T>(Func<PlanningData, Result<T>> query, CancellationToken ct = default) => Run(query, false, ct);
    public Task<Result<T>> ChangeAsync<T>(Func<PlanningData, Result<T>> command, CancellationToken ct = default) => Run(command, true, ct);

    private async Task<Result<T>> Run<T>(Func<PlanningData, Result<T>> operation, bool write, CancellationToken ct)
    {
        if (write) await Gate.WaitAsync(ct);
        try
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            var postgres = db.Database.ProviderName?.Contains("Npgsql", StringComparison.Ordinal) == true;
            await using var transaction = await db.Database.BeginTransactionAsync(write && postgres ? IsolationLevel.ReadCommitted
                : postgres ? IsolationLevel.RepeatableRead : IsolationLevel.Serializable, ct);
            // Serializes ID allocation and business validation across server processes as well.
            if (write && postgres) await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(846219350)", ct);
            if (!write) db.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
            var d = new PlanningData
            {
                Users = await db.Set<AppUser>().ToListAsync(ct),
                Teams = await db.Set<PlanningTeam>().ToListAsync(ct),
                Memberships = await db.Set<TeamMembership>().ToListAsync(ct),
                Roles = await db.Set<TeamRole>().ToListAsync(ct),
                Capacities = await db.Set<CapacityAllocation>().ToListAsync(ct),
                Leaves = await db.Set<PlanningLeave>().ToListAsync(ct),
                Cycles = await db.Set<PlanningCycle>().ToListAsync(ct),
                Sprints = await db.Set<PlanningSprint>().ToListAsync(ct),
                Products = await db.Set<PlanningProduct>().ToListAsync(ct),
                Tasks = await db.Set<PlanningTask>().ToListAsync(ct),
                Implementations = await db.Set<Implementation>().ToListAsync(ct),
                WorkLogs = await db.Set<WorkLog>().ToListAsync(ct),
                Events = await db.Set<PlanningEvent>().ToListAsync(ct),
                Jobs = await db.Set<PublicationJob>().ToListAsync(ct),
                Deliveries = await db.Set<WebhookDelivery>().ToListAsync(ct),
                Audit = await db.Set<AuditEntry>().ToListAsync(ct)
            };
            var original = write ? db.ChangeTracker.Entries().Select(e => e.Entity).ToArray() : [];
            var result = operation(d);
            if (!write || result.IsFailure) return result;
            var current = All(d).ToHashSet(ReferenceEqualityComparer.Instance);
            foreach (var item in original.Where(item => !current.Contains(item))) db.Remove(item);
            foreach (var item in current)
                if (db.Entry(item).State == EntityState.Detached) db.Add(item);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return result;
        }
        catch (Exception ex) when (ex is DbException or DbUpdateException)
        {
            logger.LogWarning("Falha de persistência: {Type}", ex.GetType().Name);
            return Result<T>.Failure(new("database.unavailable", ErrorCategory.Unavailable,
                "Não foi possível salvar ou consultar os dados. Atualize e tente novamente."));
        }
        finally { if (write) Gate.Release(); }
    }

    private static IEnumerable<object> All(PlanningData d) => d.Users.Cast<object>().Concat(d.Teams).Concat(d.Memberships)
        .Concat(d.Roles).Concat(d.Capacities).Concat(d.Leaves).Concat(d.Cycles).Concat(d.Sprints).Concat(d.Products)
        .Concat(d.Tasks).Concat(d.Implementations).Concat(d.WorkLogs).Concat(d.Events).Concat(d.Jobs).Concat(d.Deliveries).Concat(d.Audit);
}
