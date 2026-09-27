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
        var service = new DashboardService(gateway, new FakeConnection(), clock, "America/Sao_Paulo");
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
        var service = new DashboardService(gateway, new FakeConnection(), new FixedClock(new DateTimeOffset(2026, 9, 25, 12, 0, 0, TimeSpan.Zero)), "America/Sao_Paulo");
        var sprint = new Sprint("1", "Sprint", "Projeto\\Sprint", new(2026, 9, 21), new(2026, 9, 25));

        var result = await service.GetDashboardAsync("project", "team", sprint, null, true);

        Assert.True(result.IsFailure);
        Assert.Equal("devops.incomplete", result.Error!.Code);
        Assert.True(gateway.Forced);
    }

    private sealed class FakeGateway : IDevOpsGateway
    {
        public bool Fail { get; init; }
        public bool Forced { get; private set; }
        public Task<Result<IReadOnlyList<NamedItem>>> ListProjectsAsync(CancellationToken cancellationToken) => Task.FromResult(Result<IReadOnlyList<NamedItem>>.Success([]));
        public Task<Result<IReadOnlyList<NamedItem>>> ListTeamsAsync(string projectId, CancellationToken cancellationToken) => Task.FromResult(Result<IReadOnlyList<NamedItem>>.Success([]));
        public Task<Result<IReadOnlyList<Sprint>>> ListSprintsAsync(string projectId, string teamId, CancellationToken cancellationToken) => Task.FromResult(Result<IReadOnlyList<Sprint>>.Success([]));
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
        public bool IsConfigured { get; private set; } = true;
        public Result Configure(string organization, string personalAccessToken)
        { Organization = organization; IsConfigured = true; return Result.Success(); }
        public void Disconnect() => IsConfigured = false;
    }
}
