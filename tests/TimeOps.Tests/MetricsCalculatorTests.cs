using TimeOps.Domain;

namespace TimeOps.Tests;

public sealed class MetricsCalculatorTests
{
    private static readonly Person Ana = new("ana-id", "Ana");
    private static readonly Sprint Sprint = new("sprint-1", "Sprint 1", "Projeto\\Sprint 1", new(2026, 9, 14), new(2026, 9, 25));

    [Theory]
    [InlineData(80, 0, 100)]
    [InlineData(64, -16, 80)]
    [InlineData(96, 16, 120)]
    public void Compara_completed_com_80_horas_esperadas(decimal completed, decimal difference, decimal coverage)
    {
        var snapshot = Snapshot([Task(1, Ana, completed)]);
        var result = MetricsCalculator.Calculate(snapshot, new(2026, 9, 25), new(2026, 9, 27));

        Assert.True(result.IsSuccess);
        Assert.Equal(80, result.Value.People.Single().Expected);
        Assert.Equal(completed, result.Value.People.Single().Completed);
        Assert.Equal(difference, result.Value.People.Single().Difference);
        Assert.Equal(coverage, result.Value.People.Single().Coverage);
    }

    [Fact]
    public void Desconta_folgas_sobrepostas_uma_vez_e_soma_atividades()
    {
        var off = new DayRange(new(2026, 9, 16), new(2026, 9, 16));
        var snapshot = Snapshot([], [new(Ana, [6, 2], [off])], [off]);
        var result = MetricsCalculator.Calculate(snapshot, new(2026, 9, 25), new(2026, 9, 27));

        Assert.Equal(9, result.Value.People.Single().ElapsedDays);
        Assert.Equal(72, result.Value.People.Single().Expected);
    }

    [Fact]
    public void Data_de_ontem_deixa_hoje_na_capacidade_futura()
    {
        var snapshot = Snapshot([], sprint: new("s", "Sprint", "Projeto\\Sprint", new(2026, 9, 21), new(2026, 9, 25)));
        var result = MetricsCalculator.Calculate(snapshot, new(2026, 9, 23), new(2026, 9, 24));

        Assert.Equal(24, result.Value.People.Single().Expected);
        Assert.Equal(16, result.Value.People.Single().FutureCapacity);
    }

    [Fact]
    public void Ausencia_de_capacidade_nao_assume_oito_horas()
    {
        var joao = new Person("joao-id", "João");
        var result = MetricsCalculator.Calculate(Snapshot([Task(2, joao, 12)]), new(2026, 9, 25), new(2026, 9, 27));

        Assert.Null(result.Value.People.Single(person => person.Person!.Id == joao.Id).Expected);
        Assert.Null(result.Value.Team.Coverage);
        Assert.Contains(result.Value.Warnings, warning => warning.Code == "capacity.missing");
    }

    [Fact]
    public void Tasks_duplicadas_nao_somam_duas_vezes_e_sem_responsavel_entra_no_total()
    {
        var task = Task(1, Ana, 8);
        var result = MetricsCalculator.Calculate(Snapshot([task, task, Task(2, null, 4)]), new(2026, 9, 25), new(2026, 9, 27));

        Assert.Equal(12, result.Value.Team.Completed);
        Assert.Equal(2, result.Value.Team.TaskCount);
        Assert.Equal(4, result.Value.Unassigned!.Completed);
        Assert.Null(result.Value.Team.Coverage);
    }

    [Fact]
    public void Pessoas_homonimas_mantem_totais_separados()
    {
        var outraAna = new Person("outra-ana", "Ana");
        var result = MetricsCalculator.Calculate(Snapshot([Task(1, Ana, 10), Task(2, outraAna, 20)],
            [new(Ana, [8], []), new(outraAna, [4], [])]), new(2026, 9, 25), new(2026, 9, 27));

        Assert.Equal(2, result.Value.People.Count);
        Assert.Contains(result.Value.People, item => item.Person!.Id == "outra-ana" && item.Completed == 20);
    }

    [Fact]
    public void Completed_indisponivel_nao_impede_as_outras_metricas_enquanto_vazio_gera_aviso_e_zero()
    {
        var empty = MetricsCalculator.Calculate(Snapshot([Task(1, Ana, null)]), new(2026, 9, 25), new(2026, 9, 27));
        Assert.Equal(0, empty.Value.People.Single().Completed);
        Assert.Contains(empty.Value.Warnings, warning => warning.Code == "completed.empty");

        var absent = MetricsCalculator.Calculate(Snapshot([], fields: new(false, true, true)), new(2026, 9, 25), new(2026, 9, 27));
        Assert.True(absent.IsSuccess);
        Assert.Null(absent.Value.Team.Completed);
        Assert.Equal(80, absent.Value.Team.Expected);
        Assert.Contains(absent.Value.Warnings, warning => warning.Code == "field.completed.missing");
    }

    [Fact]
    public void Cobertura_da_equipe_usa_somas_e_nao_media_de_percentuais()
    {
        var bob = new Person("bob", "Bob");
        var result = MetricsCalculator.Calculate(Snapshot([Task(1, Ana, 40), Task(2, bob, 0)],
            [new(Ana, [4], []), new(bob, [8], [])]), new(2026, 9, 25), new(2026, 9, 27));
        Assert.Equal(40m / 120m * 100m, result.Value.Team.Coverage);
    }

    private static SprintSnapshot Snapshot(IReadOnlyList<TaskWork> tasks, IReadOnlyList<MemberCapacity>? capacities = null,
        IReadOnlyList<DayRange>? daysOff = null, Sprint? sprint = null, EffortFields? fields = null)
        => new(sprint ?? Sprint, new(new HashSet<DayOfWeek> { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday }, daysOff ?? []),
            capacities ?? [new(Ana, [8], [])], tasks, fields ?? new(true, true, true), DateTimeOffset.UtcNow);

    private static TaskWork Task(int id, Person? owner, decimal? completed)
        => new(id, "Task " + id, owner, "Active", "InProgress", "Projeto", Sprint.Path, completed, 10, 2, "https://example.com/" + id);
}
