using System.Text.Json;
using TimeOps.Domain;

namespace TimeOps.Application;

public sealed class PlanningService(IPlanningStore store, ICurrentActor actor, IGitHubProjectsGateway github, TimeProvider clock)
{
    public Task<Result<PlanningWorkspace>> WorkspaceAsync(CancellationToken ct = default) => store.ReadAsync(d =>
    {
        var user = Current(d);
        if (user is null) return Denied<PlanningWorkspace>();
        var teams = d.Teams.Where(t => user.IsAdmin || d.Memberships.Any(m => m.TeamId == t.Id && m.UserId == user.Id)).OrderBy(t => t.Id).ToArray();
        var ids = teams.Select(t => t.Id).ToHashSet();
        var tasks = d.Tasks.Where(t => ids.Contains(t.TeamId)).ToList();
        var taskIds = tasks.Select(t => t.Id).ToHashSet();
        var implementations = d.Implementations.Where(i => taskIds.Contains(i.TaskId)).ToList();
        var implementationIds = implementations.Select(i => i.Id).ToHashSet();
        var memberships = d.Memberships.Where(m => ids.Contains(m.TeamId)).ToList();
        var users = memberships.Select(m => m.UserId).Append(user.Id).ToHashSet();
        var data = new PlanningData
        {
            Users = d.Users.Where(u => user.IsAdmin || users.Contains(u.Id)).ToList(),
            Teams = teams.ToList(),
            Memberships = memberships,
            Roles = d.Roles.Where(r => ids.Contains(r.TeamId)).ToList(),
            Capacities = d.Capacities.Where(c => ids.Contains(c.TeamId)).ToList(),
            Leaves = d.Leaves.Where(l => ids.Contains(l.TeamId)).ToList(),
            Cycles = d.Cycles.Where(c => ids.Contains(c.TeamId)).ToList(),
            Sprints = d.Sprints.Where(s => ids.Contains(s.TeamId)).ToList(),
            Products = d.Products.Where(p => ids.Contains(p.TeamId)).ToList(),
            Tasks = tasks,
            Implementations = implementations,
            WorkLogs = d.WorkLogs.Where(w => implementationIds.Contains(w.ImplementationId)).ToList(),
            Events = d.Events.Where(e => implementationIds.Contains(e.ImplementationId)).ToList()
        };
        return Result<PlanningWorkspace>.Success(new(user, teams, data));
    }, ct);

    public Task<Result<int>> SaveTeamAsync(int id, string name, string timeZoneId, int workingDays, CancellationToken ct = default)
        => store.ChangeAsync(d =>
        {
            if (id == 0 ? Current(d)?.IsAdmin != true : !Can(d, id, true)) return Denied<int>();
            if (string.IsNullOrWhiteSpace(name) || name.Length > 100 || workingDays is < 1 or > 127)
                return Invalid<int>("team.invalid", "Informe nome e ao menos um dia de trabalho.");
            try { _ = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId); }
            catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
            { return Invalid<int>("timezone.invalid", "Informe um fuso horário válido."); }
            var team = id == 0 ? new PlanningTeam { Id = Next(d.Teams.Select(t => t.Id)) } : d.Teams.Single(t => t.Id == id);
            if (id == 0) d.Teams.Add(team);
            team.Name = name.Trim(); team.TimeZoneId = timeZoneId; team.WorkingDaysMask = workingDays;
            Audit(d, "equipe.salvar", $"team:{team.Id}", null, team);
            return Result<int>.Success(team.Id);
        }, ct);

    public async Task<Result<string>> SaveUserAsync(string login, bool admin, bool active, CancellationToken ct = default)
    {
        var allowed = await store.ReadAsync(d => Result<bool>.Success(Current(d)?.IsAdmin == true), ct);
        if (allowed.IsFailure) return Result<string>.Failure(allowed.Error!);
        if (!allowed.Value) return Denied<string>();
        var found = await github.FindUserAsync(login.Trim(), ct);
        if (found.IsFailure) return Result<string>.Failure(found.Error!);
        return await store.ChangeAsync(d =>
        {
            if (Current(d)?.IsAdmin != true) return Denied<string>();
            var existing = d.Users.SingleOrDefault(u => u.Id == found.Value.Id);
            if (existing?.IsAdmin == true && (!admin || !active) && !d.Users.Any(u => u.Id != existing.Id && u.IsAdmin && u.IsActive))
                return Invalid<string>("admin.last", "Mantenha ao menos um administrador ativo.");
            var user = existing ?? found.Value;
            if (existing is null) d.Users.Add(user);
            user.Login = found.Value.Login; user.Name = found.Value.Name; user.IsAdmin = admin; user.IsActive = active;
            Audit(d, "usuario.salvar", $"user:{user.Id}", null, user);
            return Result<string>.Success(user.Id);
        }, ct);
    }

    public Task<Result<int>> SaveMembershipAsync(int teamId, string userId, TeamAccess access, CancellationToken ct = default)
        => store.ChangeAsync(d =>
        {
            if (!Can(d, teamId, true)) return Denied<int>();
            if (!Enum.IsDefined(access) || !d.Users.Any(u => u.Id == userId && u.IsActive))
                return Invalid<int>("member.invalid", "Selecione uma pessoa ativa, cadastrada por um administrador.");
            var member = d.Memberships.SingleOrDefault(m => m.TeamId == teamId && m.UserId == userId);
            if (member is null) { member = new() { Id = Next(d.Memberships.Select(m => m.Id)), TeamId = teamId, UserId = userId }; d.Memberships.Add(member); }
            member.Access = access;
            Audit(d, "integrante.salvar", $"membership:{member.Id}", null, member);
            return Result<int>.Success(member.Id);
        }, ct);

    public Task<Result<int>> SaveRoleAsync(int teamId, string name, CancellationToken ct = default) => store.ChangeAsync(d =>
    {
        if (!Can(d, teamId, true)) return Denied<int>();
        name = name.Trim();
        if (name.Length is < 1 or > 100 || d.Roles.Any(r => r.TeamId == teamId && r.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            return Invalid<int>("role.invalid", "Informe uma função com nome único na equipe.");
        var role = new TeamRole { Id = Next(d.Roles.Select(r => r.Id)), TeamId = teamId, Name = name };
        d.Roles.Add(role); Audit(d, "funcao.criar", $"role:{role.Id}", null, role);
        return Result<int>.Success(role.Id);
    }, ct);

    public Task<Result<int>> SaveCapacityAsync(int teamId, string userId, int roleId, int? sprintId, decimal hours, CancellationToken ct = default)
        => store.ChangeAsync(d =>
        {
            if (!Can(d, teamId, true)) return Denied<int>();
            if (!Member(d, teamId, userId) || !d.Roles.Any(r => r.Id == roleId && r.TeamId == teamId) || !SprintBelongs(d, teamId, sprintId))
                return Invalid<int>("capacity.context", "Pessoa, função e sprint devem pertencer à mesma equipe.");
            if (hours < 0 || hours > 24) return Invalid<int>("capacity.hours", "A capacidade deve estar entre zero e 24 horas por dia.");
            var allocation = d.Capacities.SingleOrDefault(c => c.TeamId == teamId && c.UserId == userId && c.RoleId == roleId && c.SprintId == sprintId);
            var other = d.Capacities.Where(c => c.TeamId == teamId && c.UserId == userId && c.SprintId == sprintId && c != allocation).Sum(c => c.HoursPerDay);
            if (other + hours > 24) return Invalid<int>("capacity.total", "A soma das funções não pode ultrapassar 24 horas por dia.");
            if (allocation is null)
            {
                allocation = new() { Id = Next(d.Capacities.Select(c => c.Id)), TeamId = teamId, UserId = userId, RoleId = roleId, SprintId = sprintId };
                d.Capacities.Add(allocation);
            }
            var before = allocation.HoursPerDay; allocation.HoursPerDay = hours;
            Audit(d, "capacidade.salvar", $"capacity:{allocation.Id}", before, allocation);
            return Result<int>.Success(allocation.Id);
        }, ct);

    public Task<Result<int>> AddLeaveAsync(int teamId, string? userId, DateOnly start, DateOnly end, CancellationToken ct = default)
        => store.ChangeAsync(d =>
        {
            if (!Can(d, teamId, true)) return Denied<int>();
            if (end < start || userId is not null && !Member(d, teamId, userId))
                return Invalid<int>("leave.invalid", "Confira as datas e o integrante da folga.");
            var leave = new PlanningLeave { Id = Next(d.Leaves.Select(l => l.Id)), TeamId = teamId, UserId = userId, Start = start, End = end };
            d.Leaves.Add(leave); Audit(d, "folga.criar", $"leave:{leave.Id}", null, leave);
            return Result<int>.Success(leave.Id);
        }, ct);

    public Task<Result<bool>> RemoveLeaveAsync(int leaveId, CancellationToken ct = default) => store.ChangeAsync(d =>
    {
        var leave = d.Leaves.SingleOrDefault(l => l.Id == leaveId);
        if (leave is null) return Missing<bool>();
        if (!Can(d, leave.TeamId, true)) return Denied<bool>();
        d.Leaves.Remove(leave); Audit(d, "folga.remover", $"leave:{leave.Id}", leave, null);
        return Result<bool>.Success(true);
    }, ct);

    public Task<Result<int>> CreateCycleAsync(int teamId, string name, IReadOnlyList<SprintInput> sprints, CancellationToken ct = default)
        => store.ChangeAsync(d =>
        {
            if (!Can(d, teamId, true)) return Denied<int>();
            if (string.IsNullOrWhiteSpace(name) || name.Length > 100 || sprints.Count is < 1 or > 100)
                return Invalid<int>("cycle.invalid", "Informe o nome do ciclo e entre uma e 100 sprints.");
            var existing = d.Sprints.Where(s => s.TeamId == teamId).ToList();
            foreach (var input in sprints)
            {
                var valid = PlanningRules.ValidateSprint(input.Start, input.End, existing);
                if (valid.IsFailure) return Result<int>.Failure(valid.Error!);
                if (string.IsNullOrWhiteSpace(input.Name) || input.Name.Length > 100) return Invalid<int>("sprint.name", "Informe o nome de cada sprint.");
                existing.Add(new() { Start = input.Start, End = input.End });
            }
            var cycle = new PlanningCycle { Id = Next(d.Cycles.Select(c => c.Id)), TeamId = teamId, Name = name.Trim() };
            d.Cycles.Add(cycle);
            foreach (var input in sprints) AddSprint(d, cycle, input);
            Audit(d, "ciclo.criar", $"cycle:{cycle.Id}", null, cycle);
            return Result<int>.Success(cycle.Id);
        }, ct);

    public Task<Result<int>> SaveSprintAsync(int cycleId, int sprintId, SprintInput input, CancellationToken ct = default)
        => store.ChangeAsync(d =>
        {
            var cycle = d.Cycles.SingleOrDefault(c => c.Id == cycleId);
            if (cycle is null) return Missing<int>();
            if (!Can(d, cycle.TeamId, true)) return Denied<int>();
            var sprint = d.Sprints.SingleOrDefault(s => s.Id == sprintId && s.CycleId == cycleId);
            if (sprintId != 0 && sprint is null) return Missing<int>();
            if (string.IsNullOrWhiteSpace(input.Name) || input.Name.Length > 100) return Invalid<int>("sprint.name", "Informe o nome da sprint.");
            var valid = PlanningRules.ValidateSprint(input.Start, input.End, d.Sprints.Where(s => s.TeamId == cycle.TeamId && s.Id != sprintId));
            if (valid.IsFailure) return Result<int>.Failure(valid.Error!);
            if (d.WorkLogs.Any(w => w.SprintId == sprintId && (w.Date < input.Start || w.Date > input.End)))
                return Invalid<int>("sprint.logs", "As novas datas deixariam apontamentos fora da sprint.");
            if (sprint is null) sprint = AddSprint(d, cycle, input);
            else { sprint.Name = input.Name.Trim(); sprint.Start = input.Start; sprint.End = input.End; }
            Audit(d, "sprint.salvar", $"sprint:{sprint.Id}", null, sprint);
            return Result<int>.Success(sprint.Id);
        }, ct);

    public Task<Result<bool>> RemoveSprintAsync(int sprintId, CancellationToken ct = default) => store.ChangeAsync(d =>
    {
        var sprint = d.Sprints.SingleOrDefault(s => s.Id == sprintId);
        if (sprint is null) return Missing<bool>();
        if (!Can(d, sprint.TeamId, true)) return Denied<bool>();
        if (d.Implementations.Any(i => i.SprintId == sprintId) || d.WorkLogs.Any(w => w.SprintId == sprintId)
            || d.Events.Any(e => e.FromSprintId == sprintId || e.ToSprintId == sprintId))
            return Invalid<bool>("sprint.used", "A sprint possui planejamento ou histórico e não pode ser removida.");
        d.Capacities.RemoveAll(c => c.SprintId == sprintId); d.Sprints.Remove(sprint);
        Audit(d, "sprint.remover", $"sprint:{sprint.Id}", sprint, null);
        return Result<bool>.Success(true);
    }, ct);

    public async Task<Result<GitHubProjectInfo>> InspectProductAsync(int teamId, string repository, int projectNumber, CancellationToken ct = default)
    {
        var access = await store.ReadAsync(d => Result<bool>.Success(Can(d, teamId, true)), ct);
        if (access.IsFailure) return Result<GitHubProjectInfo>.Failure(access.Error!);
        return access.Value ? await github.InspectAsync(repository.Trim(), projectNumber, ct) : Denied<GitHubProjectInfo>();
    }

    public async Task<Result<int>> CreateProductAsync(int teamId, string name, string repository, int projectNumber,
        IReadOnlyDictionary<string, string> mapping, CancellationToken ct = default)
    {
        var inspected = await InspectProductAsync(teamId, repository, projectNumber, ct);
        if (inspected.IsFailure) return Result<int>.Failure(inspected.Error!);
        return await store.ChangeAsync(d =>
        {
            if (!Can(d, teamId, true)) return Denied<int>();
            var info = inspected.Value;
            if (string.IsNullOrWhiteSpace(name) || name.Length > 100 || mapping.Count != info.StatusOptions.Count
                || info.StatusOptions.Keys.Any(k => !mapping.ContainsKey(k))
                || mapping.Values.Any(v => v is not ("Proposed" or "InProgress" or "Completed" or "Removed")))
                return Invalid<int>("product.mapping", "Informe o nome e a categoria de cada opção de status do Project.");
            if (d.Products.Any(p => p.TeamId == teamId && p.RepositoryNodeId == info.RepositoryId && p.ProjectNodeId == info.ProjectId))
                return Invalid<int>("product.duplicate", "Este repositório e Project já estão cadastrados nesta equipe.");
            var product = new PlanningProduct
            {
                Id = Next(d.Products.Select(p => p.Id)),
                TeamId = teamId,
                Name = name.Trim(),
                Repository = repository.Trim(),
                ProjectNumber = projectNumber,
                RepositoryNodeId = info.RepositoryId,
                ProjectNodeId = info.ProjectId,
                ProjectUrl = info.ProjectUrl,
                StatusFieldId = info.StatusFieldId,
                StatusMappingJson = JsonSerializer.Serialize(mapping)
            };
            d.Products.Add(product); Audit(d, "produto.criar", $"product:{product.Id}", null, product);
            return Result<int>.Success(product.Id);
        }, ct);
    }

    public Task<Result<int>> CreateTaskAsync(int teamId, Guid requestId, string title, string description,
        IReadOnlyList<ImplementationInput> implementations, CancellationToken ct = default) => store.ChangeAsync(d =>
    {
        if (!Can(d, teamId)) return Denied<int>();
        if (requestId == Guid.Empty || string.IsNullOrWhiteSpace(title) || title.Length > 200 || description.Length > 20000
            || implementations.Count is < 1 or > 100 || implementations.Select(i => i.ProductId).Distinct().Count() != implementations.Count)
            return Invalid<int>("task.invalid", "Informe um título e selecione produtos distintos para a tarefa.");
        var duplicate = d.Tasks.SingleOrDefault(t => t.RequestId == requestId);
        if (duplicate is not null)
            return duplicate.TeamId == teamId && duplicate.CreatedBy == actor.UserId && duplicate.Title == title.Trim()
                && duplicate.Description == description && d.Implementations.Where(i => i.TaskId == duplicate.Id).Select(i => i.ProductId).Order()
                    .SequenceEqual(implementations.Select(i => i.ProductId).Order())
                ? Result<int>.Success(duplicate.Id) : Invalid<int>("task.request", "Esta solicitação já foi utilizada. Recarregue a página.");
        foreach (var input in implementations)
        {
            if (!d.Products.Any(p => p.Id == input.ProductId && p.TeamId == teamId) || !SprintBelongs(d, teamId, input.SprintId))
                return Invalid<int>("task.context", "Produtos e sprints devem pertencer à equipe da tarefa.");
            foreach (var hours in new[] { input.Original, input.Remaining })
            { var valid = PlanningRules.ValidateHours(hours); if (valid.IsFailure) return Result<int>.Failure(valid.Error!); }
        }
        var now = clock.GetUtcNow();
        var task = new PlanningTask
        {
            Id = Next(d.Tasks.Select(t => t.Id)),
            RequestId = requestId,
            TeamId = teamId,
            Title = title.Trim(),
            Description = description,
            CreatedBy = actor.UserId,
            CreatedAt = now
        };
        d.Tasks.Add(task);
        foreach (var input in implementations)
        {
            var implementation = new Implementation
            {
                Id = Next(d.Implementations.Select(i => i.Id)),
                TaskId = task.Id,
                ProductId = input.ProductId,
                SprintId = input.SprintId,
                Original = input.Original,
                Remaining = input.Remaining
            };
            d.Implementations.Add(implementation);
            Event(d, implementation, "remaining", null, input.SprintId, 0, input.Remaining);
            Event(d, implementation, "scope", null, input.SprintId, 0, input.Remaining);
            d.Jobs.Add(new() { Id = Next(d.Jobs.Select(j => j.Id)), ImplementationId = implementation.Id, NextAttemptAt = now });
        }
        Audit(d, "tarefa.criar", $"task:{task.Id}", null, task);
        return Result<int>.Success(task.Id);
    }, ct);

    public Task<Result<int>> UpdateTaskAsync(int taskId, string title, string description, CancellationToken ct = default) => store.ChangeAsync(d =>
    {
        var task = d.Tasks.SingleOrDefault(t => t.Id == taskId);
        if (task is null) return Missing<int>();
        if (!Can(d, task.TeamId, true) && !(Can(d, task.TeamId) && task.CreatedBy == actor.UserId)) return Denied<int>();
        if (string.IsNullOrWhiteSpace(title) || title.Length > 200 || description.Length > 20000) return Invalid<int>("task.invalid", "Confira título e descrição.");
        var before = JsonSerializer.Serialize(task); task.Title = title.Trim(); task.Description = description;
        Audit(d, "tarefa.editar", $"task:{task.Id}", before, task);
        return Result<int>.Success(task.Id);
    }, ct);

    public Task<Result<int>> UpdateImplementationAsync(int id, int version, int? sprintId, decimal? original, decimal? remaining, CancellationToken ct = default)
        => store.ChangeAsync(d =>
        {
            var implementation = d.Implementations.SingleOrDefault(i => i.Id == id);
            if (implementation is null) return Missing<int>();
            var task = d.Tasks.Single(t => t.Id == implementation.TaskId);
            if (!CanEdit(d, task.TeamId, implementation)) return Denied<int>();
            if (version != implementation.Version) return Conflict<int>();
            if (!SprintBelongs(d, task.TeamId, sprintId)) return Invalid<int>("implementation.sprint", "Selecione uma sprint da equipe.");
            foreach (var hours in new[] { original, remaining })
            { var valid = PlanningRules.ValidateHours(hours); if (valid.IsFailure) return Result<int>.Failure(valid.Error!); }
            if (implementation.SprintId != sprintId)
                Event(d, implementation, "scope", implementation.SprintId, sprintId, implementation.Remaining, remaining);
            if (implementation.Remaining != remaining)
                Event(d, implementation, "remaining", implementation.SprintId, sprintId, implementation.Remaining, remaining);
            if (implementation.Original != original)
                Event(d, implementation, "original", implementation.SprintId, sprintId, implementation.Original, original);
            implementation.SprintId = sprintId; implementation.Original = original; implementation.Remaining = remaining; implementation.Version++;
            Audit(d, "implementacao.planejar", $"implementation:{id}", null, implementation);
            return Result<int>.Success(id);
        }, ct);

    public Task<Result<int>> SaveWorkLogAsync(int id, int version, int implementationId, int sprintId, string userId,
        DateOnly date, decimal hours, bool voided = false, CancellationToken ct = default) => store.ChangeAsync(d =>
    {
        var implementation = d.Implementations.SingleOrDefault(i => i.Id == implementationId);
        if (implementation is null) return Missing<int>();
        var task = d.Tasks.Single(t => t.Id == implementation.TaskId);
        if (!Can(d, task.TeamId) || userId != actor.UserId && !Can(d, task.TeamId, true)) return Denied<int>();
        var sprint = d.Sprints.SingleOrDefault(s => s.Id == sprintId && s.TeamId == task.TeamId);
        var team = d.Teams.Single(t => t.Id == task.TeamId);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), TimeZoneInfo.FindSystemTimeZoneById(team.TimeZoneId)).DateTime);
        if (!Member(d, task.TeamId, userId) || sprint is null || date < sprint.Start || date > sprint.End || date > today || hours <= 0 || hours > 24)
            return Invalid<int>("worklog.invalid", "Informe uma pessoa da equipe, data não futura dentro da sprint e horas maiores que zero e até 24.");
        var log = d.WorkLogs.SingleOrDefault(w => w.Id == id);
        if (id != 0 && log is null) return Missing<int>();
        if (log is not null && (log.ImplementationId != implementationId || log.UserId != userId || log.SprintId != sprintId))
            return Invalid<int>("worklog.identity", "Corrija data e horas ou anule o apontamento e registre outro.");
        if (log is not null && log.Version != version) return Conflict<int>();
        var total = d.WorkLogs.Where(w => w.UserId == userId && w.Date == date && w.Id != id && !w.Voided).Sum(w => w.Hours);
        if (!voided && total + hours > 24) return Invalid<int>("worklog.day", "O total de apontamentos da pessoa não pode ultrapassar 24 horas no dia.");
        var before = log is null ? null : JsonSerializer.Serialize(log);
        if (log is null)
        {
            log = new()
            {
                Id = Next(d.WorkLogs.Select(w => w.Id)),
                ImplementationId = implementationId,
                SprintId = sprintId,
                UserId = userId,
                CreatedAt = clock.GetUtcNow()
            }; d.WorkLogs.Add(log);
        }
        var oldHours = log.Voided ? 0 : log.Hours;
        Event(d, implementation, "completed", sprintId, sprintId, oldHours, voided ? 0 : hours);
        log.Date = date; log.Hours = hours; log.Voided = voided; log.Version++;
        Audit(d, voided ? "apontamento.anular" : "apontamento.salvar", $"worklog:{log.Id}", before, log);
        return Result<int>.Success(log.Id);
    }, ct);

    public async Task<Result<int>> UpdateProductMappingAsync(int productId, string name, IReadOnlyDictionary<string, string> mapping, CancellationToken ct = default)
    {
        var found = await store.ReadAsync(d =>
        {
            var p = d.Products.SingleOrDefault(p => p.Id == productId);
            return p is null ? Missing<PlanningProduct>() : !Can(d, p.TeamId, true) ? Denied<PlanningProduct>() : Result<PlanningProduct>.Success(p);
        }, ct);
        if (found.IsFailure) return Result<int>.Failure(found.Error!);
        var inspected = await github.InspectAsync(found.Value.Repository, found.Value.ProjectNumber, ct);
        if (inspected.IsFailure) return Result<int>.Failure(inspected.Error!);
        return await store.ChangeAsync(d =>
        {
            var p = d.Products.Single(p => p.Id == productId);
            if (!Can(d, p.TeamId, true)) return Denied<int>();
            if (string.IsNullOrWhiteSpace(name) || name.Length > 100 || mapping.Count != inspected.Value.StatusOptions.Count
                || inspected.Value.StatusOptions.Keys.Any(k => !mapping.ContainsKey(k))
                || mapping.Values.Any(v => v is not ("Proposed" or "InProgress" or "Completed" or "Removed")))
                return Invalid<int>("product.mapping", "Defina a categoria de cada status consultado no GitHub.");
            p.Name = name.Trim(); p.StatusFieldId = inspected.Value.StatusFieldId; p.StatusMappingJson = JsonSerializer.Serialize(mapping);
            foreach (var job in d.Jobs.Where(j => d.Implementations.Any(i => i.Id == j.ImplementationId && i.ProductId == productId)))
            { job.Completed = false; job.NextAttemptAt = clock.GetUtcNow(); job.Attempts = 0; }
            Audit(d, "produto.mapear", $"product:{p.Id}", null, p);
            return Result<int>.Success(p.Id);
        }, ct);
    }

    public async Task<Result<bool>> AuthorizeRecreationAsync(int implementationId, CancellationToken ct = default)
    {
        var found = await store.ReadAsync(d =>
        {
            var i = d.Implementations.SingleOrDefault(i => i.Id == implementationId);
            if (i is null) return Missing<(Implementation, PlanningProduct)>();
            var p = d.Products.Single(p => p.Id == i.ProductId);
            return Can(d, p.TeamId, true) ? Result<(Implementation, PlanningProduct)>.Success((i, p)) : Denied<(Implementation, PlanningProduct)>();
        }, ct);
        if (found.IsFailure) return Result<bool>.Failure(found.Error!);
        var (implementation, product) = found.Value;
        var checkedIssue = await github.FindIssueAsync(product.Repository, implementation.CorrelationId, ct);
        if (checkedIssue.IsFailure) return Result<bool>.Failure(checkedIssue.Error!);
        if (checkedIssue.Value is not null) return Invalid<bool>("github.exists", "A issue já existe. Use Sincronizar para recuperar o vínculo.");
        return await store.ChangeAsync(d =>
        {
            var i = d.Implementations.Single(i => i.Id == implementationId);
            if (!Can(d, product.TeamId, true)) return Denied<bool>();
            if (i.PublicationState != PublicationState.Uncertain || i.IssueNodeId is not null)
                return Invalid<bool>("github.state", "A publicação mudou. Atualize os dados antes de continuar.");
            i.PublicationState = PublicationState.Pending; i.SyncError = null;
            var job = d.Jobs.Single(j => j.ImplementationId == i.Id); job.Completed = false; job.NextAttemptAt = clock.GetUtcNow(); job.Attempts = 0;
            Audit(d, "publicacao.recriar", $"implementation:{i.Id}", null, i.CorrelationId);
            return Result<bool>.Success(true);
        }, ct);
    }

    public Task<Result<bool>> RequestSyncAsync(int implementationId, CancellationToken ct = default) => store.ChangeAsync(d =>
    {
        var implementation = d.Implementations.SingleOrDefault(i => i.Id == implementationId);
        if (implementation is null) return Missing<bool>();
        if (!Can(d, d.Tasks.Single(t => t.Id == implementation.TaskId).TeamId)) return Denied<bool>();
        var job = d.Jobs.Single(j => j.ImplementationId == implementationId);
        job.Completed = false; job.NextAttemptAt = clock.GetUtcNow(); job.Attempts = 0;
        return Result<bool>.Success(true);
    }, ct);

    private PlanningSprint AddSprint(PlanningData d, PlanningCycle cycle, SprintInput input)
    {
        var sprint = new PlanningSprint
        {
            Id = Next(d.Sprints.Select(s => s.Id)),
            CycleId = cycle.Id,
            TeamId = cycle.TeamId,
            Name = input.Name.Trim(),
            Start = input.Start,
            End = input.End
        };
        d.Sprints.Add(sprint);
        foreach (var capacity in d.Capacities.Where(c => c.TeamId == cycle.TeamId && c.SprintId is null).ToArray())
            d.Capacities.Add(new()
            {
                Id = Next(d.Capacities.Select(c => c.Id)),
                TeamId = cycle.TeamId,
                SprintId = sprint.Id,
                UserId = capacity.UserId,
                RoleId = capacity.RoleId,
                HoursPerDay = capacity.HoursPerDay
            });
        return sprint;
    }

    private void Event(PlanningData d, Implementation i, string kind, int? from, int? to, decimal? before, decimal? after)
        => d.Events.Add(new()
        {
            Id = Next(d.Events.Select(e => e.Id)),
            ImplementationId = i.Id,
            Kind = kind,
            FromSprintId = from,
            ToSprintId = to,
            Before = before,
            After = after,
            ActorId = actor.UserId,
            At = clock.GetUtcNow()
        });
    private void Audit(PlanningData d, string action, string entity, object? before, object? after)
        => d.Audit.Add(new()
        {
            Id = Next(d.Audit.Select(a => a.Id)),
            ActorId = actor.UserId,
            Action = action,
            Entity = entity,
            BeforeJson = JsonSerializer.Serialize(before),
            AfterJson = JsonSerializer.Serialize(after),
            At = clock.GetUtcNow()
        });
    private AppUser? Current(PlanningData d) => d.Users.SingleOrDefault(u => u.Id == actor.UserId && u.IsActive);
    private bool Can(PlanningData d, int teamId, bool manager = false)
        => d.Teams.Any(t => t.Id == teamId) && Current(d) is { } user && (user.IsAdmin
            || d.Memberships.Any(m => m.TeamId == teamId && m.UserId == user.Id && (!manager || m.Access == TeamAccess.Manager)));
    private bool CanEdit(PlanningData d, int teamId, Implementation i) => Can(d, teamId, true)
        || Can(d, teamId) && JsonSerializer.Deserialize<string[]>(i.AssigneeIdsJson)!.Contains(actor.UserId);
    private static bool Member(PlanningData d, int teamId, string userId)
        => d.Users.Any(u => u.Id == userId && u.IsActive) && d.Memberships.Any(m => m.TeamId == teamId && m.UserId == userId);
    private static bool SprintBelongs(PlanningData d, int teamId, int? sprintId) => sprintId is null || d.Sprints.Any(s => s.Id == sprintId && s.TeamId == teamId);
    public static int Next(IEnumerable<int> ids) => ids.DefaultIfEmpty().Max() + 1;
    private static Result<T> Invalid<T>(string code, string message) => Result<T>.Failure(new(code, ErrorCategory.Validation, message));
    private static Result<T> Denied<T>() => Result<T>.Failure(new("access.denied", ErrorCategory.Authorization, "Você não tem permissão para esta operação."));
    private static Result<T> Missing<T>() => Result<T>.Failure(new("record.missing", ErrorCategory.NotFound, "O registro não foi encontrado."));
    private static Result<T> Conflict<T>() => Result<T>.Failure(new("record.conflict", ErrorCategory.Validation, "Os dados foram alterados por outra pessoa. Atualize e tente novamente."));
}
