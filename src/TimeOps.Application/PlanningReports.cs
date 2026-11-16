using System.Text.Json;
using TimeOps.Domain;

namespace TimeOps.Application;

public sealed class PlanningReports(PlanningService planning, TimeProvider clock)
{
    public async Task<Result<Dashboard>> DashboardAsync(int teamId, int sprintId, DateOnly reference, int? productId = null, CancellationToken ct = default)
    {
        var workspace = await planning.WorkspaceAsync(ct);
        return workspace.IsFailure ? Result<Dashboard>.Failure(workspace.Error!) : BuildDashboard(workspace.Value.Data, teamId, sprintId, reference, clock.GetUtcNow(), productId);
    }

    public static Result<Dashboard> BuildDashboard(PlanningData d, int teamId, int sprintId, DateOnly reference, DateTimeOffset now, int? productId = null)
    {
        var team = d.Teams.SingleOrDefault(t => t.Id == teamId);
        var sprint = d.Sprints.SingleOrDefault(s => s.Id == sprintId && s.TeamId == teamId);
        if (team is null || sprint is null) return Missing<Dashboard>();
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, TimeZoneInfo.FindSystemTimeZoneById(team.TimeZoneId)).DateTime);
        var products = d.Products.Where(p => p.TeamId == teamId && (productId is null || p.Id == productId)).ToDictionary(p => p.Id);
        var all = d.Implementations.Where(i => products.ContainsKey(i.ProductId)).ToArray();
        var ids = all.Select(i => i.Id).ToHashSet();
        var logs = d.WorkLogs.Where(w => w.SprintId == sprintId && !w.Voided && ids.Contains(w.ImplementationId)).ToArray();
        var included = all.Where(i => i.SprintId == sprintId || logs.Any(w => w.ImplementationId == i.Id)).ToArray();
        var tasks = d.Tasks.ToDictionary(t => t.Id);
        Person PersonFor(string id) => new(id, d.Users.SingleOrDefault(u => u.Id == id)?.Name ?? "Pessoa sem cadastro");
        var work = included.Select(i =>
        {
            var assignees = JsonSerializer.Deserialize<string[]>(i.AssigneeIdsJson)!;
            var person = assignees.Length == 1 && d.Memberships.Any(m => m.TeamId == teamId && m.UserId == assignees[0]) ? PersonFor(assignees[0]) : null;
            return new TaskWork(i.Id, $"{products[i.ProductId].Name} · {i.GitHubTitle ?? tasks[i.TaskId].Title}", person, i.StatusName, i.StateCategory,
                team.Name, sprint.Name, logs.Where(l => l.ImplementationId == i.Id).Sum(l => l.Hours),
                i.SprintId == sprintId ? i.Original : 0, i.SprintId == sprintId ? i.Remaining : 0,
                i.IssueUrl ?? "/tarefas", i.TaskId);
        }).ToArray();
        var capacities = d.Capacities.Where(c => c.TeamId == teamId && c.SprintId == sprintId).GroupBy(c => c.UserId)
            .Select(g => new MemberCapacity(PersonFor(g.Key), g.Select(c => c.HoursPerDay).ToArray(),
                d.Leaves.Where(l => l.TeamId == teamId && l.UserId == g.Key).Select(l => new DayRange(l.Start, l.End)).ToArray())).ToList();
        foreach (var member in d.Memberships.Where(m => m.TeamId == teamId && capacities.All(c => c.Person.Id != m.UserId)))
            capacities.Add(new(PersonFor(member.UserId), [], d.Leaves.Where(l => l.TeamId == teamId && l.UserId == member.UserId).Select(l => new DayRange(l.Start, l.End)).ToArray()));
        var parents = included.GroupBy(i => i.TaskId).Select(g =>
        {
            var periods = d.Implementations.Where(i => i.TaskId == g.Key && i.SprintId is not null)
                .Select(i => d.Sprints.Single(s => s.Id == i.SprintId)).ToArray();
            return new ParentWorkItem(g.Key, tasks[g.Key].Title, "Feature", null, $"/tarefas?team={teamId}",
                periods.Length == 0 ? null : periods.Min(s => s.Start), periods.Length == 0 ? null : periods.Max(s => s.End));
        }).ToArray();
        var calendar = new TeamCalendar(Enum.GetValues<DayOfWeek>().Where(day => (team.WorkingDaysMask & 1 << (int)day) != 0).ToHashSet(),
            d.Leaves.Where(l => l.TeamId == teamId && l.UserId is null).Select(l => new DayRange(l.Start, l.End)).ToArray());
        var snapshot = new SprintSnapshot(new(sprint.Id.ToString(), sprint.Name, sprint.Name, sprint.Start, sprint.End), calendar,
            capacities, work, new(true, true, true), now, parents);
        var calculated = MetricsCalculator.Calculate(snapshot, reference, today);
        if (calculated.IsFailure) return calculated;
        var dashboard = calculated.Value;
        var people = dashboard.People.ToList();
        foreach (var userId in logs.Select(l => l.UserId).Distinct().Where(id => people.All(p => p.Person!.Id != id)))
            people.Add(new(PersonFor(userId), null, 0, 0, 0, null, null, null, 0, 0, 0, null, null, null, [],
                [new("capacity.missing", $"Capacidade ausente para {PersonFor(userId).Name}.")]));
        people = people.Select(p =>
        {
            var completed = logs.Where(l => l.UserId == p.Person!.Id).Sum(l => l.Hours);
            var traced = work.Where(w => w.Assignee?.Id == p.Person!.Id || logs.Any(l => l.UserId == p.Person!.Id && l.ImplementationId == w.Id)).Select(w => w with { Completed = logs.Where(l => l.UserId == p.Person!.Id && l.ImplementationId == w.Id).Sum(l => l.Hours), Original = w.Assignee?.Id == p.Person!.Id ? w.Original : 0, Remaining = w.Assignee?.Id == p.Person!.Id ? w.Remaining : 0 }).ToArray();
            return p with
            {
                Completed = completed,
                Difference = p.Expected is { } expected ? completed - expected : null,
                Coverage = p.Expected > 0 ? completed / p.Expected * 100 : null,
                Tasks = traced
            };
        }).OrderBy(p => p.Person!.Name).ToList();
        var warnings = dashboard.Warnings.Concat(people.SelectMany(p => p.Warnings)).ToList();
        foreach (var i in included)
        {
            if (i.SyncError is not null) warnings.Add(new("github.sync", $"{products[i.ProductId].Name}: {i.SyncError}"));
            if (i.PublicationState != PublicationState.Published) warnings.Add(new("github.pending", $"Implementação #{i.Id}: publicação pendente no GitHub."));
            if (i.Archived) warnings.Add(new("github.archived", $"Implementação #{i.Id}: item arquivado no Project."));
        }
        var total = logs.Sum(l => l.Hours);
        var allHaveCapacity = people.All(p => p.Expected.HasValue) && dashboard.Unassigned is null;
        decimal? Sum(Func<PersonMetrics, decimal?> f) => allHaveCapacity ? people.Sum(p => f(p) ?? 0) : null;
        var expectedTotal = Sum(p => p.Expected);
        var teamMetrics = dashboard.Team with
        {
            Completed = total,
            Expected = expectedTotal,
            TotalCapacity = Sum(p => p.TotalCapacity),
            FutureCapacity = Sum(p => p.FutureCapacity),
            Difference = expectedTotal is { } expected ? total - expected : null,
            Coverage = expectedTotal > 0 ? total / expectedTotal * 100 : null,
            FutureBalance = Sum(p => p.FutureCapacity) is { } future && dashboard.Team.Remaining is { } remaining ? future - remaining : null
        };
        var unassigned = dashboard.Unassigned is null ? null : dashboard.Unassigned with { Completed = 0, Difference = null, Coverage = null };
        return Result<Dashboard>.Success(dashboard with { People = people, Unassigned = unassigned, Team = teamMetrics, Warnings = warnings.Distinct().ToArray() });
    }

    public async Task<Result<Burndown>> BurndownAsync(int teamId, int sprintId, CancellationToken ct = default)
    {
        var workspace = await planning.WorkspaceAsync(ct);
        return workspace.IsFailure ? Result<Burndown>.Failure(workspace.Error!) : BuildBurndown(workspace.Value.Data, teamId, sprintId, clock.GetUtcNow());
    }

    public static Result<Burndown> BuildBurndown(PlanningData d, int teamId, int sprintId, DateTimeOffset now)
    {
        var team = d.Teams.SingleOrDefault(t => t.Id == teamId);
        var sprint = d.Sprints.SingleOrDefault(s => s.Id == sprintId && s.TeamId == teamId);
        if (team is null || sprint is null) return Missing<Burndown>();
        var zone = TimeZoneInfo.FindSystemTimeZoneById(team.TimeZoneId);
        DateTimeOffset Start(DateOnly day) { var date = day.ToDateTime(TimeOnly.MinValue); while (zone.IsInvalidTime(date)) date = date.AddMinutes(1); return new(date, zone.GetUtcOffset(date)); }
        var ids = d.Implementations.Where(i => d.Products.Any(p => p.Id == i.ProductId && p.TeamId == teamId)).Select(i => i.Id).ToHashSet();
        var events = d.Events.Where(e => ids.Contains(e.ImplementationId) && e.At <= now && e.Kind is "scope" or "remaining").OrderBy(e => e.At).ThenBy(e => e.Id).ToArray();
        var scope = new Dictionary<int, int?>(); var remaining = new Dictionary<int, decimal?>(); var index = 0;
        void ApplyUntil(DateTimeOffset until)
        {
            while (index < events.Length && events[index].At <= until)
            {
                var e = events[index++];
                if (e.Kind == "scope") scope[e.ImplementationId] = e.ToSprintId;
                remaining[e.ImplementationId] = e.After;
            }
        }
        decimal? Total() => scope.Where(p => p.Value == sprintId).All(p => remaining.GetValueOrDefault(p.Key).HasValue)
            ? scope.Where(p => p.Value == sprintId).Sum(p => remaining[p.Key]!.Value) : null;
        ApplyUntil(Start(sprint.Start).AddTicks(-1));
        var initial = Total();
        if (initial is null) return Result<Burndown>.Failure(new("burndown.incomplete", ErrorCategory.Incomplete, "Preencha o restante das implementações; o histórico contém valores ausentes."));
        var days = Enumerable.Range(0, sprint.End.DayNumber - sprint.Start.DayNumber + 1).Select(sprint.Start.AddDays).ToArray();
        bool Working(DateOnly day) => (team.WorkingDaysMask & 1 << (int)day.DayOfWeek) != 0
            && !d.Leaves.Any(l => l.TeamId == teamId && l.UserId is null && day >= l.Start && day <= l.End);
        var useful = days.Count(Working);
        if (useful == 0) return Result<Burndown>.Failure(new("burndown.calendar", ErrorCategory.Validation, "A sprint precisa de ao menos um dia de trabalho."));
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);
        var points = new List<BurndownPoint> { new(sprint.Start, initial.Value, now >= Start(sprint.Start) ? initial : null, true) };
        var elapsed = 0;
        foreach (var day in days)
        {
            if (Working(day)) elapsed++;
            ApplyUntil(day <= today ? (Start(day.AddDays(1)).AddTicks(-1) < now ? Start(day.AddDays(1)).AddTicks(-1) : now) : now);
            var real = day <= today ? Total() : null;
            if (day <= today && real is null) return Result<Burndown>.Failure(new("burndown.incomplete", ErrorCategory.Incomplete, "O histórico do restante possui valores ausentes. A curva completa está indisponível."));
            points.Add(new(day, initial.Value * (useful - elapsed) / useful, real));
        }
        return Result<Burndown>.Success(new(new(sprint.Id.ToString(), sprint.Name, sprint.Name, sprint.Start, sprint.End), now, initial.Value,
            events.Where(e => e.FromSprintId == sprintId || e.ToSprintId == sprintId).Select(e => e.ImplementationId).Distinct().Count(), points,
            [new("burndown.scope", "O histórico local inclui replanejamento entre sprints. O dia atual é parcial; datas futuras não têm valor real.")]));
    }

    public async Task<Result<TaskExportData>> ExportAsync(int teamId, int sprintId, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var result = await planning.WorkspaceAsync(ct);
        if (result.IsFailure) return Result<TaskExportData>.Failure(result.Error!);
        var d = result.Value.Data;
        var sprint = d.Sprints.SingleOrDefault(s => s.Id == sprintId && s.TeamId == teamId);
        var team = d.Teams.SingleOrDefault(t => t.Id == teamId);
        if (sprint is null || team is null) return Missing<TaskExportData>();
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), TimeZoneInfo.FindSystemTimeZoneById(team.TimeZoneId)).DateTime);
        if (to < from || from < sprint.Start || to > sprint.End || to > today)
            return Result<TaskExportData>.Failure(new("export.dates", ErrorCategory.Validation, "Selecione datas dentro da sprint, até hoje."));
        var rows = d.WorkLogs.Where(w => w.SprintId == sprintId && !w.Voided && w.Date >= from && w.Date <= to).Select(w =>
        {
            var i = d.Implementations.Single(i => i.Id == w.ImplementationId); var task = d.Tasks.Single(t => t.Id == i.TaskId);
            var product = d.Products.Single(p => p.Id == i.ProductId); var person = d.Users.Single(u => u.Id == w.UserId);
            return new TaskExportRow(i.Id, w.Id, new(person.Id, person.Name), $"{product.Name} · {i.GitHubTitle ?? task.Title}", task.Title, w.CreatedAt, w.Date, w.Hours);
        }).OrderBy(r => r.Date).ThenBy(r => r.AuthorName).ToArray();
        return Result<TaskExportData>.Success(new(new(sprint.Id.ToString(), sprint.Name, sprint.Name, sprint.Start, sprint.End), from, to, clock.GetUtcNow(), rows));
    }
    private static Result<T> Missing<T>() => Result<T>.Failure(new("context.invalid", ErrorCategory.NotFound, "Selecione equipe e sprint disponíveis para seu acesso."));
}
