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

    private sealed class FakeGateway : IDevOpsGateway
    {
        public bool Fail { get; init; }
        public bool Forced { get; private set; }
        public DateTimeOffset? HistoryFrom { get; private set; }
        public DateTimeOffset? HistoryTo { get; private set; }
        public Task<Result<IReadOnlyList<NamedItem>>> ListProjectsAsync(CancellationToken cancellationToken) => Task.FromResult(Result<IReadOnlyList<NamedItem>>.Success([]));
        public Task<Result<IReadOnlyList<NamedItem>>> ListTeamsAsync(string projectId, CancellationToken cancellationToken) => Task.FromResult(Result<IReadOnlyList<NamedItem>>.Success([]));
        public Task<Result<IReadOnlyList<Sprint>>> ListSprintsAsync(string projectId, string teamId, CancellationToken cancellationToken) => Task.FromResult(Result<IReadOnlyList<Sprint>>.Success([]));
        public Task<Result<EffortHistory>> LoadEffortHistoryAsync(string projectId, string teamId, Sprint sprint, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
        {
            HistoryFrom = from;
            HistoryTo = to;
            return Task.FromResult(Result<EffortHistory>.Success(new(from, to, to, [])));
        }
        public Task<Result<SprintSnapshot>> LoadSnapshotAsync(string projectId, string teamId, Sprint sprint, bool forceRefresh, CancellationToken cancellationToken)
        {
            Forced = forceRefresh;
            if (Fail) return Task.FromResult(Result<SprintSnapshot>.Failure(new("devops.incomplete", ErrorCategory.Incomplete, "Consulta incompleta.")));
            var person = new Person("id", "Ana");
            var snapshot = new SprintSnapshot(sprint,
                new(new HashSet<DayOfWeek> { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday }, []),
                [new(person, [8], [])], [], new(true, true, true), DateTimeOffset.UtcNow);
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
        public SavedConnection? Saved { get; set; }
        public Result<SavedConnection?> Read() => Result<SavedConnection?>.Success(Saved);
        public Result Save(SavedConnection connection)
        {
            if (FailSave) return Result.Failure(new("store.failed", ErrorCategory.Unavailable, "Falha ao salvar."));
            Saved = connection;
            return Result.Success();
        }
        public Result Delete() { Saved = null; return Result.Success(); }
    }
}
