using TimeOps.Application;
using TimeOps.Domain;

namespace TimeOps.Tests;

public sealed class DashboardServiceTests
{
    [Fact]
    public async Task Referencia_padrao_e_ontem_no_fuso_configurado()
    {
        var gateway = new FakeGateway();
        var clock = new FixedClock(new DateTimeOffset(2026, 9, 25, 12, 0, 0, TimeSpan.Zero));
        var service = new DashboardService(gateway, new FakeConnection(), new FakeStore(), clock, "America/Sao_Paulo");
        var sprint = new Sprint("1", "Sprint", "Projeto\\Sprint", new(2026, 9, 21), new(2026, 9, 25));

        var result = await service.GetDashboardAsync("project", "team", sprint, null);

        Assert.True(result.IsSuccess);
        Assert.Equal(new DateOnly(2026, 9, 24), result.Value.ReferenceDate);
        Assert.Equal(32, result.Value.Team.Expected);
    }

    [Fact]
    public async Task Falha_da_integracao_e_propagada_com_result()
    {
        var gateway = new FakeGateway { Fail = true };
        var service = new DashboardService(gateway, new FakeConnection(), new FakeStore(), new FixedClock(new DateTimeOffset(2026, 9, 25, 12, 0, 0, TimeSpan.Zero)), "America/Sao_Paulo");
        var sprint = new Sprint("1", "Sprint", "Projeto\\Sprint", new(2026, 9, 21), new(2026, 9, 25));

        var result = await service.GetDashboardAsync("project", "team", sprint, null, true);

        Assert.True(result.IsFailure);
        Assert.Equal("devops.incomplete", result.Error!.Code);
        Assert.True(gateway.Forced);
    }

    [Fact]
    public void Conexao_salva_e_restaurada_e_pode_ser_esquecida()
    {
        var store = new FakeStore();
        var connection = new FakeConnection();
        connection.Disconnect();
        var service = new DashboardService(new FakeGateway(), connection, store, TimeProvider.System, "America/Sao_Paulo");

        Assert.True(service.ConfigureConnection("org", "pat-de-teste", true).IsSuccess);
        Assert.Equal("pat-de-teste", store.Saved!.PersonalAccessToken);
        connection.Disconnect();
        Assert.True(service.RestoreConnection().Value);
        Assert.True(service.HasConnection);
        Assert.Equal("pat-de-teste", connection.Token);

        Assert.True(service.ForgetConnection().IsSuccess);
        Assert.False(service.HasConnection);
        Assert.Null(store.Saved);
        Assert.False(service.RestoreConnection().Value);
    }

    [Fact]
    public void Conexao_sem_persistencia_apaga_credencial_anterior()
    {
        var store = new FakeStore { Saved = new SavedConnection("org-antiga", "pat-antigo") };
        var service = new DashboardService(new FakeGateway(), new FakeConnection(), store, TimeProvider.System, "America/Sao_Paulo");

        Assert.True(service.ConfigureConnection("org-nova", "pat-novo", false).IsSuccess);
        Assert.Null(store.Saved);
        Assert.True(service.HasConnection);
    }

    [Fact]
    public void Falha_ao_salvar_nao_deixa_conexao_ativa()
    {
        var store = new FakeStore { FailSave = true };
        var service = new DashboardService(new FakeGateway(), new FakeConnection(), store, TimeProvider.System, "America/Sao_Paulo");

        var result = service.ConfigureConnection("org", "pat-de-teste", true);

        Assert.True(result.IsFailure);
        Assert.False(service.HasConnection);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(30)]
    public async Task Historico_consulta_o_periodo_selecionado_exato(int days)
    {
        var now = new DateTimeOffset(2026, 9, 27, 15, 30, 0, TimeSpan.Zero);
        var gateway = new FakeGateway();
        var service = new DashboardService(gateway, new FakeConnection(), new FakeStore(), new FixedClock(now), "America/Sao_Paulo");
        var sprint = new Sprint("s1", "Sprint", "Projeto\\Sprint", null, null);

        var result = await service.GetEffortHistoryAsync("project", "team", sprint, days);

        Assert.True(result.IsSuccess);
        Assert.Equal(now.AddDays(-days), gateway.HistoryFrom);
        Assert.Equal(now, gateway.HistoryTo);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(2)]
    [InlineData(31)]
    public async Task Historico_rejeita_periodos_invalidos_sem_consultar_gateway(int days)
    {
        var gateway = new FakeGateway();
        var service = new DashboardService(gateway, new FakeConnection(), new FakeStore(), TimeProvider.System, "America/Sao_Paulo");
        var sprint = new Sprint("s1", "Sprint", "Projeto\\Sprint", null, null);

        var result = await service.GetEffortHistoryAsync("project", "team", sprint, days);

        Assert.True(result.IsFailure);
        Assert.Equal("history.window", result.Error!.Code);
        Assert.Null(gateway.HistoryFrom);
    }

    [Fact]
    public async Task Burndown_consulta_desde_meia_noite_local_e_ancora_no_snapshot_mesmo_acima_de_30_dias()
    {
        var collectedAt = new DateTimeOffset(2026, 9, 27, 15, 0, 0, TimeSpan.Zero);
        var gateway = new FakeGateway { SnapshotCollectedAt = collectedAt };
        var service = new DashboardService(gateway, new FakeConnection(), new FakeStore(),
            new FixedClock(collectedAt.AddHours(2)), "America/Sao_Paulo");
        var sprint = new Sprint("s1", "Sprint", "Projeto\\Sprint", new(2026, 8, 20), new(2026, 9, 30));

        var result = await service.GetBurndownAsync("project", "team", sprint);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal(new DateTimeOffset(2026, 8, 20, 3, 0, 0, TimeSpan.Zero), gateway.HistoryFrom);
        Assert.Equal(collectedAt, gateway.HistoryTo);
        Assert.NotNull(gateway.BurndownSnapshot);
        Assert.Equal(sprint, gateway.BurndownSnapshot.Sprint);
        Assert.Equal(collectedAt, gateway.BurndownSnapshot.CollectedAt);
        Assert.Equal(collectedAt, result.Value.CollectedAt);
    }

    [Fact]
    public async Task Burndown_rejeita_contexto_e_datas_invalidas_e_propaga_falhas()
    {
        var gateway = new FakeGateway { Fail = true };
        var service = new DashboardService(gateway, new FakeConnection(), new FakeStore(), TimeProvider.System, "America/Sao_Paulo");
        var sprint = new Sprint("s1", "Sprint", "Projeto\\Sprint", new(2026, 9, 21), new(2026, 9, 25));

        Assert.Equal("context.invalid", (await service.GetBurndownAsync("", "team", sprint)).Error!.Code);
        Assert.Equal("sprint.dates", (await service.GetBurndownAsync("project", "team", sprint with { End = null })).Error!.Code);
        Assert.Equal("devops.incomplete", (await service.GetBurndownAsync("project", "team", sprint)).Error!.Code);
        Assert.Null(gateway.HistoryFrom);
    }

    [Fact]
    public async Task Burndown_nao_consulta_historico_antes_do_inicio()
    {
        var collectedAt = new DateTimeOffset(2026, 9, 20, 15, 0, 0, TimeSpan.Zero);
        var gateway = new FakeGateway { SnapshotCollectedAt = collectedAt };
        var service = new DashboardService(gateway, new FakeConnection(), new FakeStore(), new FixedClock(collectedAt), "America/Sao_Paulo");
        var sprint = new Sprint("s1", "Sprint", "Projeto\\Sprint", new(2026, 9, 21), new(2026, 9, 25));

        var result = await service.GetBurndownAsync("project", "team", sprint);

        Assert.True(result.IsSuccess);
        Assert.Null(gateway.HistoryFrom);
        Assert.All(result.Value.Points, point => Assert.Null(point.Remaining));
    }

    [Fact]
    public async Task Referencia_padrao_respeita_virada_de_dia_em_Sao_Paulo()
    {
        var clock = new FixedClock(new DateTimeOffset(2026, 9, 25, 1, 0, 0, TimeSpan.Zero));
        var service = new DashboardService(new FakeGateway(), new FakeConnection(), new FakeStore(), clock, "America/Sao_Paulo");
        var sprint = new Sprint("s1", "Sprint", "Sprint", new(2026, 9, 21), new(2026, 9, 25));

        var result = await service.GetDashboardAsync("project", "team", sprint, null);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal(new DateOnly(2026, 9, 24), service.Today);
        Assert.Equal(new DateOnly(2026, 9, 23), result.Value.ReferenceDate);
        Assert.Equal(24, result.Value.Team.Expected);
    }

    [Fact]
    public async Task Referencia_futura_nao_consulta_gateway_e_hoje_e_permitido()
    {
        var gateway = new FakeGateway();
        var clock = new FixedClock(new DateTimeOffset(2026, 9, 25, 12, 0, 0, TimeSpan.Zero));
        var service = new DashboardService(gateway, new FakeConnection(), new FakeStore(), clock, "America/Sao_Paulo");
        var sprint = new Sprint("s1", "Sprint", "Sprint", new(2026, 9, 21), new(2026, 9, 25));

        var future = await service.GetDashboardAsync("project", "team", sprint, new(2026, 9, 26));
        Assert.Equal("reference.future", future.Error!.Code);
        Assert.Equal(0, gateway.SnapshotCalls);

        var today = await service.GetDashboardAsync("project", "team", sprint, new(2026, 9, 25));
        Assert.True(today.IsSuccess, today.Error?.Message);
        Assert.Equal(new DateOnly(2026, 9, 25), today.Value.ReferenceDate);
        Assert.Equal(1, gateway.SnapshotCalls);
    }

    [Theory]
    [InlineData("", "team", "s1")]
    [InlineData("project", " ", "s1")]
    [InlineData("project", "team", "")]
    public async Task Contexto_invalido_nao_consulta_painel_historico_nem_burndown(string project, string team, string sprintId)
    {
        var gateway = new FakeGateway();
        var service = new DashboardService(gateway, new FakeConnection(), new FakeStore(), TimeProvider.System, "America/Sao_Paulo");
        var sprint = new Sprint(sprintId, "Sprint", "Sprint", new(2026, 9, 21), new(2026, 9, 25));

        Assert.Equal("context.invalid", (await service.GetDashboardAsync(project, team, sprint, null)).Error!.Code);
        Assert.Equal("context.invalid", (await service.GetEffortHistoryAsync(project, team, sprint, 7)).Error!.Code);
        Assert.Equal("context.invalid", (await service.GetBurndownAsync(project, team, sprint)).Error!.Code);
        Assert.Equal(0, gateway.SnapshotCalls);
        Assert.Null(gateway.HistoryFrom);
    }

    [Fact]
    public async Task Fuso_inexistente_retorna_erro_sem_consultar_gateway()
    {
        var gateway = new FakeGateway();
        var service = new DashboardService(gateway, new FakeConnection(), new FakeStore(), TimeProvider.System, "TimeOps/Invalid");
        var sprint = new Sprint("s1", "Sprint", "Sprint", new(2026, 9, 21), new(2026, 9, 25));

        Assert.Equal("timezone.invalid", (await service.GetDashboardAsync("project", "team", sprint, null)).Error!.Code);
        Assert.Equal("timezone.invalid", (await service.GetBurndownAsync("project", "team", sprint)).Error!.Code);
        Assert.Equal(0, gateway.SnapshotCalls);
    }

    [Fact]
    public async Task Burndown_na_abertura_exata_nao_consulta_historico_e_mostra_inicio()
    {
        var collectedAt = new DateTimeOffset(2026, 9, 21, 3, 0, 0, TimeSpan.Zero);
        var gateway = new FakeGateway { SnapshotCollectedAt = collectedAt };
        var service = new DashboardService(gateway, new FakeConnection(), new FakeStore(), new FixedClock(collectedAt), "America/Sao_Paulo");
        var sprint = new Sprint("s1", "Sprint", "Sprint", new(2026, 9, 21), new(2026, 9, 25));

        var result = await service.GetBurndownAsync("project", "team", sprint);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Null(gateway.HistoryFrom);
        Assert.True(result.Value.Points[0].IsBaseline);
        Assert.Equal(0m, result.Value.Points[0].Remaining);
        Assert.All(result.Value.Points.Skip(2), point => Assert.Null(point.Remaining));
    }

    [Fact]
    public async Task Burndown_propaga_falha_do_historico_sem_curva_parcial()
    {
        var error = new Error("devops.incomplete", ErrorCategory.Incomplete, "Histórico incompleto.");
        var collectedAt = new DateTimeOffset(2026, 9, 23, 12, 0, 0, TimeSpan.Zero);
        var gateway = new FakeGateway { SnapshotCollectedAt = collectedAt, HistoryError = error };
        var service = new DashboardService(gateway, new FakeConnection(), new FakeStore(), new FixedClock(collectedAt), "America/Sao_Paulo");
        var sprint = new Sprint("s1", "Sprint", "Sprint", new(2026, 9, 21), new(2026, 9, 25));

        var result = await service.GetBurndownAsync("project", "team", sprint);

        Assert.True(result.IsFailure);
        Assert.Same(error, result.Error);
        Assert.NotNull(gateway.HistoryFrom);
    }

    [Fact]
    public void Falha_ao_remover_credencial_anterior_desconecta_e_preserva_erro()
    {
        var saved = new SavedConnection("org-antiga", "pat-antigo");
        var store = new FakeStore { Saved = saved, FailDelete = true };
        var connection = new FakeConnection();
        var service = new DashboardService(new FakeGateway(), connection, store, TimeProvider.System, "America/Sao_Paulo");

        var result = service.ConfigureConnection("org-nova", "pat-novo", false);

        Assert.Equal("store.delete", result.Error!.Code);
        Assert.False(service.HasConnection);
        Assert.Equal("", connection.Token);
        Assert.Equal(saved, store.Saved);
    }

    [Fact]
    public void Esquecer_desconecta_mesmo_quando_exclusao_falha()
    {
        var store = new FakeStore { Saved = new("org", "pat-sintetico"), FailDelete = true };
        var connection = new FakeConnection();
        var service = new DashboardService(new FakeGateway(), connection, store, TimeProvider.System, "America/Sao_Paulo");
        service.ConfigureConnection("org", "pat-sintetico", true);

        var result = service.ForgetConnection();

        Assert.Equal("store.delete", result.Error!.Code);
        Assert.False(service.HasConnection);
        Assert.Equal("", connection.Token);
        Assert.NotNull(store.Saved);
    }

    private sealed class FakeGateway : IDevOpsGateway
    {
        public bool Fail { get; init; }
        public Error? HistoryError { get; init; }
        public int SnapshotCalls { get; private set; }
        public DateTimeOffset SnapshotCollectedAt { get; init; } = DateTimeOffset.UtcNow;
        public bool Forced { get; private set; }
        public DateTimeOffset? HistoryFrom { get; private set; }
        public DateTimeOffset? HistoryTo { get; private set; }
        public SprintSnapshot? BurndownSnapshot { get; private set; }
        public Task<Result<IReadOnlyList<NamedItem>>> ListProjectsAsync(CancellationToken cancellationToken) => Task.FromResult(Result<IReadOnlyList<NamedItem>>.Success([]));
        public Task<Result<IReadOnlyList<NamedItem>>> ListTeamsAsync(string projectId, CancellationToken cancellationToken) => Task.FromResult(Result<IReadOnlyList<NamedItem>>.Success([]));
        public Task<Result<IReadOnlyList<Sprint>>> ListSprintsAsync(string projectId, string teamId, CancellationToken cancellationToken) => Task.FromResult(Result<IReadOnlyList<Sprint>>.Success([]));
        public Task<Result<EffortHistory>> LoadEffortHistoryAsync(string projectId, string teamId, Sprint sprint, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
        {
            HistoryFrom = from;
            HistoryTo = to;
            if (HistoryError is not null) return Task.FromResult(Result<EffortHistory>.Failure(HistoryError));
            return Task.FromResult(Result<EffortHistory>.Success(new(from, to, to, [])));
        }
        public Task<Result<EffortHistory>> LoadBurndownHistoryAsync(string projectId, string teamId, SprintSnapshot snapshot,
            DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
        {
            BurndownSnapshot = snapshot;
            return LoadEffortHistoryAsync(projectId, teamId, snapshot.Sprint, from, to, cancellationToken);
        }
        public Task<Result<SprintSnapshot>> LoadSnapshotAsync(string projectId, string teamId, Sprint sprint, bool forceRefresh, CancellationToken cancellationToken)
        {
            Forced = forceRefresh;
            SnapshotCalls++;
            if (Fail) return Task.FromResult(Result<SprintSnapshot>.Failure(new("devops.incomplete", ErrorCategory.Incomplete, "Consulta incompleta.")));
            var person = new Person("id", "Ana");
            var snapshot = new SprintSnapshot(sprint,
                new(new HashSet<DayOfWeek> { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday }, []),
                [new(person, [8], [])], [], new(true, true, true), SnapshotCollectedAt);
            return Task.FromResult(Result<SprintSnapshot>.Success(snapshot));
        }
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FakeConnection : IRuntimeConnection
    {
        public string Organization { get; private set; } = "org";
        public string Token { get; private set; } = "";
        public bool IsConfigured { get; private set; } = true;
        public Result Configure(string organization, string personalAccessToken)
        { Organization = organization; Token = personalAccessToken; IsConfigured = true; return Result.Success(); }
        public void Disconnect() { IsConfigured = false; Token = ""; }
    }

    private sealed class FakeStore : IConnectionStore
    {
        public bool IsAvailable => true;
        public bool FailSave { get; init; }
        public bool FailDelete { get; init; }
        public SavedConnection? Saved { get; set; }
        public Result<SavedConnection?> Read() => Result<SavedConnection?>.Success(Saved);
        public Result Save(SavedConnection connection)
        {
            if (FailSave) return Result.Failure(new("store.failed", ErrorCategory.Unavailable, "Falha ao salvar."));
            Saved = connection;
            return Result.Success();
        }
        public Result Delete()
        {
            if (FailDelete) return Result.Failure(new("store.delete", ErrorCategory.Unavailable, "Falha ao remover."));
            Saved = null;
            return Result.Success();
        }
    }
}
