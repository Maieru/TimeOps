using System.Text.Json;
using TimeOps.Application;
using TimeOps.Domain;
namespace TimeOps.Tests;

public sealed class PlanningTests
{
    [Fact]
    public async Task Ciclo_copia_capacidades_por_funcao_sem_alterar_sprints_anteriores()
    {
        var f = new PlanningFixture();
        Assert.True((await f.Service.SaveCapacityAsync(1, "2", 1, null, 5)).IsSuccess);
        var cycle = await f.Service.CreateCycleAsync(1, "Ciclo novo", [new("Sprint A", new(2026, 12, 1), new(2026, 12, 10)), new("Sprint B", new(2026, 12, 11), new(2026, 12, 20))]);
        Assert.True(cycle.IsSuccess);
        Assert.Equal(2, f.Store.Data.Sprints.Count(s => s.CycleId == cycle.Value));
        Assert.Equal(4, f.Store.Data.Capacities.Single(c => c.UserId == "2" && c.SprintId == 1).HoursPerDay);
        Assert.All(f.Store.Data.Capacities.Where(c => c.UserId == "2" && c.SprintId > 2), c => Assert.Equal(5, c.HoursPerDay));
        var before = f.Store.Data.Sprints.Count;
        var invalid = await f.Service.CreateCycleAsync(1, "Sobreposto", [new("Inválida", new(2026, 12, 5), new(2026, 12, 15))]);
        Assert.Equal("sprint.overlap", invalid.Error!.Code); Assert.Equal(before, f.Store.Data.Sprints.Count);
    }

    [Fact]
    public async Task Capacidades_somam_funcoes_e_validam_o_limite_diario()
    {
        var f = new PlanningFixture(); var role = await f.Service.SaveRoleAsync(1, "QA");
        Assert.True((await f.Service.SaveCapacityAsync(1, "2", role.Value, 1, 2)).IsSuccess);
        Assert.Equal("capacity.total", (await f.Service.SaveCapacityAsync(1, "2", role.Value, 1, 23)).Error!.Code);
        var report = PlanningReports.BuildDashboard(f.Store.Data, 1, 1, new(2026, 11, 9), f.Clock.Now);
        Assert.Equal(6, report.Value.People.Single(p => p.Person!.Id == "2").DailyCapacity);
    }

    [Fact]
    public async Task Tarefa_com_dois_produtos_e_repeticao_da_mesma_solicitacao_nao_duplica()
    {
        var f = new PlanningFixture(); var request = Guid.NewGuid();
        var first = await f.CreateTask(request); var repeated = await f.CreateTask(request);
        Assert.Equal(first.Value, repeated.Value); Assert.Single(f.Store.Data.Tasks); Assert.Equal(2, f.Store.Data.Implementations.Count); Assert.Equal(2, f.Store.Data.Jobs.Count);
        Assert.True((await f.Sync.RunAsync()).IsSuccess);
        Assert.Equal(2, f.GitHub.Created); Assert.All(f.Store.Data.Implementations, i => Assert.Equal(1, i.IssueNumber));
        Assert.Equal(2, f.Store.Data.Implementations.Select(i => i.IssueNodeId).Distinct().Count());
    }

    [Fact]
    public async Task Falha_ao_adicionar_ao_project_reutiliza_a_issue_criada()
    {
        var f = new PlanningFixture(); f.GitHub.FailAddOnce = true; await f.CreateTask(single: true);
        await f.Sync.RunAsync(); Assert.Equal(1, f.GitHub.Created); Assert.Equal(PublicationState.IssueCreated, f.Store.Data.Implementations[0].PublicationState);
        await f.Service.RequestSyncAsync(1); await f.Sync.RunAsync();
        Assert.Equal(1, f.GitHub.Created); Assert.Equal(PublicationState.Published, f.Store.Data.Implementations[0].PublicationState);
    }

    [Fact]
    public async Task Resposta_incerta_recupera_issue_pelo_identificador_sem_criar_outra()
    {
        var f = new PlanningFixture(); f.GitHub.UncertainCreate = true; await f.CreateTask(single: true);
        await f.Sync.RunAsync(); Assert.Equal(PublicationState.Uncertain, f.Store.Data.Implementations[0].PublicationState);
        await f.Service.RequestSyncAsync(1); await f.Sync.RunAsync();
        Assert.Equal(1, f.GitHub.Created); Assert.Equal(PublicationState.Published, f.Store.Data.Implementations[0].PublicationState);
    }

    [Fact]
    public async Task Criacao_incerta_sem_issue_exige_conferencia_do_gestor()
    {
        var f = new PlanningFixture(); f.GitHub.UncertainWithoutCreation = true; await f.CreateTask(single: true);
        await f.Sync.RunAsync(); await f.Service.RequestSyncAsync(1); await f.Sync.RunAsync(); Assert.Equal(0, f.GitHub.Created);
        Assert.Equal(PublicationState.Uncertain, f.Store.Data.Implementations[0].PublicationState);
        f.Actor.UserId = "2"; Assert.Equal("access.denied", (await f.Service.AuthorizeRecreationAsync(1)).Error!.Code);
        f.Actor.UserId = "1"; Assert.True((await f.Service.AuthorizeRecreationAsync(1)).IsSuccess); await f.Sync.RunAsync(); Assert.Equal(1, f.GitHub.Created);
    }

    [Fact]
    public async Task Horas_permanecem_com_autor_e_sprint_apos_troca_de_responsavel_e_replanejamento()
    {
        var f = new PlanningFixture(); await f.CreateTask(single: true);
        Assert.True((await f.Service.SaveWorkLogAsync(0, 0, 1, 1, "2", new(2026, 11, 9), 3)).IsSuccess);
        await f.Store.ChangeAsync(d => { d.Implementations[0].AssigneeIdsJson = "[\"1\"]"; return Result<bool>.Success(true); });
        var before = PlanningReports.BuildDashboard(f.Store.Data, 1, 1, new(2026, 11, 9), f.Clock.Now).Value;
        Assert.Equal(3, before.People.Single(p => p.Person!.Id == "2").Completed); Assert.Equal(0, before.People.Single(p => p.Person!.Id == "1").Completed);
        Assert.True((await f.Service.UpdateImplementationAsync(1, 0, 2, 8, 5)).IsSuccess);
        var after = PlanningReports.BuildDashboard(f.Store.Data, 1, 1, new(2026, 11, 9), f.Clock.Now).Value;
        Assert.Equal(3, after.Team.Completed); Assert.Equal(0, after.Team.Remaining); Assert.Equal(1, f.Store.Data.WorkLogs[0].SprintId);
        var export = await f.Reports.ExportAsync(1, 1, new(2026, 11, 9), new(2026, 11, 9));
        Assert.Equal("2", export.Value.Rows.Single().Author!.Id); Assert.Equal(3, export.Value.Rows.Single().Hours);
    }

    [Fact]
    public async Task Correcao_tem_auditoria_e_detecta_edicao_concorrente()
    {
        var f = new PlanningFixture(); await f.CreateTask(single: true); f.Actor.UserId = "2";
        var first = await f.Service.SaveWorkLogAsync(0, 0, 1, 1, "2", new(2026, 11, 9), 3);
        Assert.True(first.IsSuccess);
        Assert.True((await f.Service.SaveWorkLogAsync(first.Value, 1, 1, 1, "2", new(2026, 11, 9), 4)).IsSuccess);
        Assert.Equal("record.conflict", (await f.Service.SaveWorkLogAsync(first.Value, 1, 1, 1, "2", new(2026, 11, 9), 5)).Error!.Code);
        Assert.Equal(4, f.Store.Data.WorkLogs.Single().Hours);
        Assert.True((await f.Service.SaveWorkLogAsync(first.Value, 2, 1, 1, "2", new(2026, 11, 9), 4, true)).IsSuccess);
        Assert.Equal(3, f.Store.Data.Audit.Count(a => a.Entity == $"worklog:{first.Value}"));
        Assert.Empty((await f.Reports.ExportAsync(1, 1, new(2026, 11, 9), new(2026, 11, 9))).Value.Rows);
    }

    [Fact]
    public async Task Permissoes_validam_usuario_ativo_e_equipe_em_cada_operacao()
    {
        var f = new PlanningFixture(); f.Actor.UserId = "2";
        Assert.Equal("access.denied", (await f.Service.SaveRoleAsync(1, "Admin indevido")).Error!.Code);
        await f.CreateTask(single: true);
        Assert.Equal("access.denied", (await f.Service.SaveWorkLogAsync(0, 0, 1, 1, "1", new(2026, 11, 9), 2)).Error!.Code);
        Assert.Equal("access.denied", (await f.Service.CreateTaskAsync(999, Guid.NewGuid(), "Outro time", "", [new(1, 1, 2, 2)])).Error!.Code);
        f.Store.Data.Users.Single(u => u.Id == "2").IsActive = false;
        Assert.Equal("access.denied", (await f.Service.WorkspaceAsync()).Error!.Code);
    }

    [Fact]
    public void Integrante_sem_capacidade_permanece_visivel_com_aviso()
    {
        var f = new PlanningFixture(); f.Store.Data.Capacities.RemoveAll(c => c.UserId == "2");
        var report = PlanningReports.BuildDashboard(f.Store.Data, 1, 1, new(2026, 11, 9), f.Clock.Now).Value;
        Assert.Contains(report.People, p => p.Person!.Id == "2" && p.DailyCapacity is null);
        Assert.Null(report.Team.Expected); Assert.Contains(report.Warnings, w => w.Code == "capacity.missing");
    }

    [Fact]
    public async Task Multiplos_produtos_nao_multiplicam_capacidade_e_responsaveis_ambiguos_nao_rateiam_restante()
    {
        var f = new PlanningFixture(); await f.CreateTask();
        await f.Store.ChangeAsync(d => { foreach (var i in d.Implementations) i.AssigneeIdsJson = "[\"2\"]"; return Result<bool>.Success(true); });
        var a = PlanningReports.BuildDashboard(f.Store.Data, 1, 1, new(2026, 11, 9), f.Clock.Now).Value;
        var b = PlanningReports.BuildDashboard(f.Store.Data, 1, 1, new(2026, 11, 9), f.Clock.Now, 1).Value;
        Assert.Equal(a.Team.TotalCapacity, b.Team.TotalCapacity); Assert.Equal(10, a.Team.Remaining); Assert.Equal(5, b.Team.Remaining);
        f.Store.Data.Implementations[0].AssigneeIdsJson = "[\"1\",\"2\"]";
        var ambiguous = PlanningReports.BuildDashboard(f.Store.Data, 1, 1, new(2026, 11, 9), f.Clock.Now).Value;
        Assert.Equal(5, ambiguous.Unassigned!.Remaining); Assert.Equal(10, ambiguous.Team.Remaining);
    }

    [Fact]
    public async Task Burndown_reconstroi_entrada_saida_e_restante_sem_inventar_datas_futuras()
    {
        var f = new PlanningFixture(); f.Clock.Now = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero); await f.CreateTask(single: true);
        f.Clock.Now = new(2026, 11, 4, 12, 0, 0, TimeSpan.Zero); await f.Service.UpdateImplementationAsync(1, 0, 1, 8, 3);
        f.Clock.Now = new(2026, 11, 5, 12, 0, 0, TimeSpan.Zero); await f.Service.UpdateImplementationAsync(1, 1, 2, 8, 3);
        var result = PlanningReports.BuildBurndown(f.Store.Data, 1, 1, f.Clock.Now);
        Assert.True(result.IsSuccess); Assert.Equal(5, result.Value.InitialRemaining);
        Assert.Equal(3, result.Value.Points.Single(p => p.Date == new DateOnly(2026, 11, 4) && !p.IsBaseline).Remaining);
        Assert.Equal(0, result.Value.Points.Single(p => p.Date == new DateOnly(2026, 11, 5)).Remaining);
        Assert.All(result.Value.Points.Where(p => p.Date > new DateOnly(2026, 11, 5)), p => Assert.Null(p.Remaining));
    }
}

internal sealed class PlanningFixture
{
    public MemoryPlanningStore Store { get; } = new(); public TestActor Actor { get; } = new(); public TestClock Clock { get; } = new(); public FakeGitHub GitHub { get; } = new();
    public PlanningService Service { get; }
    public PlanningReports Reports { get; }
    public GitHubSynchronization Sync { get; }
    public PlanningFixture()
    {
        Store.Data.Users = [new() { Id = "1", Name = "Gestora", Login = "gestora", IsAdmin = true }, new() { Id = "2", Name = "Integrante", Login = "integrante" }];
        Store.Data.Teams = [new() { Id = 1, Name = "Equipe" }];
        Store.Data.Memberships = [new() { Id = 1, TeamId = 1, UserId = "1", Access = TeamAccess.Manager }, new() { Id = 2, TeamId = 1, UserId = "2" }];
        Store.Data.Roles = [new() { Id = 1, TeamId = 1, Name = "Desenvolvimento" }];
        Store.Data.Cycles = [new() { Id = 1, TeamId = 1, Name = "Ciclo" }];
        Store.Data.Sprints = [new() { Id = 1, CycleId = 1, TeamId = 1, Name = "Sprint 1", Start = new(2026, 11, 2), End = new(2026, 11, 13) }, new() { Id = 2, CycleId = 1, TeamId = 1, Name = "Sprint 2", Start = new(2026, 11, 16), End = new(2026, 11, 27) }];
        Store.Data.Capacities = [new() { Id = 1, TeamId = 1, UserId = "1", RoleId = 1, HoursPerDay = 6, SprintId = 1 }, new() { Id = 2, TeamId = 1, UserId = "2", RoleId = 1, HoursPerDay = 4, SprintId = 1 }, new() { Id = 3, TeamId = 1, UserId = "2", RoleId = 1, HoursPerDay = 4 }];
        Store.Data.Products = [new() { Id = 1, TeamId = 1, Name = "Produto A", Repository = "org/a", ProjectNodeId = "PA", StatusMappingJson = "{\"todo\":\"Proposed\"}" }, new() { Id = 2, TeamId = 1, Name = "Produto B", Repository = "org/b", ProjectNodeId = "PB", StatusMappingJson = "{\"todo\":\"Proposed\"}" }];
        Service = new(Store, Actor, GitHub, Clock); Reports = new(Service, Clock); Sync = new(Store, GitHub, Clock);
    }
    public Task<Result<int>> CreateTask(Guid? request = null, bool single = false) => Service.CreateTaskAsync(1, request ?? Guid.NewGuid(), "Tarefa compartilhada", "Descrição", single ? [new(1, 1, 8, 5)] : [new(1, 1, 8, 5), new(2, 1, 8, 5)]);
}
internal sealed class TestClock : TimeProvider { public DateTimeOffset Now { get; set; } = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero); public override DateTimeOffset GetUtcNow() => Now; }
internal sealed class TestActor : ICurrentActor { public string UserId { get; set; } = "1"; }
internal sealed class MemoryPlanningStore : IPlanningStore
{
    public PlanningData Data { get; set; } = new(); private readonly SemaphoreSlim gate = new(1, 1);
    public Task<Result<T>> ReadAsync<T>(Func<PlanningData, Result<T>> query, CancellationToken ct = default) => Task.FromResult(query(Clone()));
    public async Task<Result<T>> ChangeAsync<T>(Func<PlanningData, Result<T>> command, CancellationToken ct = default) { await gate.WaitAsync(ct); try { var d = Clone(); var r = command(d); if (r.IsSuccess) Data = d; return r; } finally { gate.Release(); } }
    private PlanningData Clone() => JsonSerializer.Deserialize<PlanningData>(JsonSerializer.Serialize(Data))!;
}
internal sealed class FakeGitHub : IGitHubProjectsGateway
{
    public int Created; public bool FailAddOnce, UncertainCreate, UncertainWithoutCreation;
    private readonly Dictionary<Guid, GitHubIssue> issues = [];
    public Task<Result<AppUser>> FindUserAsync(string login, CancellationToken ct) => Task.FromResult(Result<AppUser>.Success(new() { Id = "3", Login = login, Name = login }));
    public Task<Result<GitHubProjectInfo>> InspectAsync(string repo, int number, CancellationToken ct) => Task.FromResult(Result<GitHubProjectInfo>.Success(new("repo", $"P{number}", "https://github.com/orgs/org/projects/1", "status", new Dictionary<string, string> { { "todo", "Todo" } })));
    public Task<Result<GitHubIssue?>> FindIssueAsync(string repo, Guid id, CancellationToken ct) => Task.FromResult(Result<GitHubIssue?>.Success(issues.GetValueOrDefault(id)));
    public Task<Result<GitHubIssue>> CreateIssueAsync(string repo, string title, string body, Guid id, CancellationToken ct)
    {
        if (UncertainWithoutCreation) { UncertainWithoutCreation = false; return Task.FromResult(Result<GitHubIssue>.Failure(new("github.uncertain", ErrorCategory.Unavailable, "Timeout"))); }
        Created++; var issue = new GitHubIssue("I_" + repo, 1, "https://github.com/" + repo + "/issues/1", title, ["2"]); issues[id] = issue;
        if (UncertainCreate) { UncertainCreate = false; return Task.FromResult(Result<GitHubIssue>.Failure(new("github.uncertain", ErrorCategory.Unavailable, "Timeout"))); }
        return Task.FromResult(Result<GitHubIssue>.Success(issue));
    }
    public Task<Result<string>> AddToProjectAsync(string project, string issue, CancellationToken ct) { if (FailAddOnce) { FailAddOnce = false; return Task.FromResult(Result<string>.Failure(new("github.http.503", ErrorCategory.Unavailable, "Indisponível"))); } return Task.FromResult(Result<string>.Success("ITEM_" + issue)); }
    public Task<Result<GitHubExecution>> ReadExecutionAsync(PlanningProduct p, string issue, CancellationToken ct) => Task.FromResult(Result<GitHubExecution>.Success(new(issues.Values.Single(i => i.NodeId == issue), "ITEM_" + issue, "todo", "Todo", false)));
}
