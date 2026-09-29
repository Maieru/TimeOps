using TimeOps.Domain;

namespace TimeOps.Tests;

public sealed class BurndownCalculatorTests
{
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");
    private static readonly DateTimeOffset Start = new(2026, 9, 25, 3, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Reconstroi_inicio_e_fim_do_dia_sem_inventar_valores_futuros()
    {
        var snapshot = Snapshot(6, Start.AddDays(3).AddHours(12));
        var result = Calculate(snapshot,
            Change(2, Start.AddDays(3).AddHours(10), 8, 6),
            Change(1, Start.AddHours(15), 12, 8));

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal(12, result.Value.InitialRemaining);
        Assert.Equal(new decimal?[] { 12, 8, 8, 8, 6, null }, result.Value.Points.Select(point => point.Remaining));
        Assert.Equal(new decimal[] { 12, 8, 8, 8, 4, 0 }, result.Value.Points.Select(point => point.Ideal));
    }

    [Fact]
    public void Desconta_folgas_da_equipe_da_linha_ideal()
    {
        var snapshot = Snapshot(12, Start.AddDays(3)) with
        {
            Calendar = new(WorkingDays(), [new(new(2026, 9, 28), new(2026, 9, 28))])
        };
        var result = Calculate(snapshot);

        Assert.True(result.IsSuccess);
        Assert.Equal(new decimal[] { 12, 6, 6, 6, 6, 0 }, result.Value.Points.Select(point => point.Ideal));
    }

    [Fact]
    public void Agrupa_alteracoes_pela_data_local_e_preserva_acrescimos()
    {
        var result = Calculate(Snapshot(15, Start.AddDays(1).AddHours(1)),
            Change(1, new(2026, 9, 26, 2, 30, 0, TimeSpan.Zero), 10, 15));

        Assert.True(result.IsSuccess);
        Assert.Equal(10, result.Value.InitialRemaining);
        Assert.Equal(15, result.Value.Points[1].Remaining); // 23:30 on Friday in São Paulo.
        Assert.Equal(15, result.Value.Points[2].Remaining);
    }

    [Fact]
    public void Task_criada_durante_iteracao_comeca_com_zero()
    {
        var result = Calculate(Snapshot(8, Start.AddDays(3)), Change(1, Start.AddDays(2), null, 8));

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value.InitialRemaining);
        Assert.Equal(0, result.Value.Points[1].Remaining);
        Assert.Equal(8, result.Value.Points[3].Remaining);
        Assert.Contains(result.Value.Warnings, warning => warning.Code == "burndown.zero");
    }

    [Fact]
    public void Alteracoes_apos_encerramento_nao_mudam_valor_final_da_iteracao()
    {
        var result = Calculate(Snapshot(2, Start.AddDays(6)),
            Change(1, Start.AddDays(3), 10, 5), Change(2, Start.AddDays(5), 5, 2));

        Assert.True(result.IsSuccess);
        Assert.Equal(10, result.Value.InitialRemaining);
        Assert.Equal(5, result.Value.Points[^1].Remaining);
        Assert.Equal(0, result.Value.Points[^1].Ideal);
    }

    [Fact]
    public void Iteracao_futura_mostra_apenas_planejamento()
    {
        var result = Calculate(Snapshot(12, Start.AddDays(-1)));

        Assert.True(result.IsSuccess);
        Assert.All(result.Value.Points, point => Assert.Null(point.Remaining));
        Assert.Equal(12, result.Value.InitialRemaining);
        Assert.Contains(result.Value.Warnings, warning => warning.Code == "burndown.future");
    }

    [Fact]
    public void Remaining_vazio_conta_como_zero_e_gera_aviso_sem_impedir_o_grafico()
    {
        var result = Calculate(Snapshot(null, Start.AddDays(1)));

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal(1, result.Value.TaskCount);
        Assert.Equal(0, result.Value.InitialRemaining);
        Assert.All(result.Value.Points, point => Assert.Equal(0, point.Ideal));
        Assert.All(result.Value.Points.Take(3), point => Assert.Equal(0, point.Remaining));
        Assert.All(result.Value.Points.Skip(3), point => Assert.Null(point.Remaining));
        Assert.Contains(result.Value.Warnings, warning => warning.Code == "burndown.remaining.empty");
    }

    [Fact]
    public void Tasks_sem_remaining_nao_alteram_totais_das_tasks_preenchidas()
    {
        var snapshot = Snapshot(10, Start.AddDays(1));
        snapshot = snapshot with { Tasks = [snapshot.Tasks[0], snapshot.Tasks[0] with { Id = 2, Remaining = null }] };

        var result = Calculate(snapshot);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal(2, result.Value.TaskCount);
        Assert.Equal(10, result.Value.InitialRemaining);
        Assert.Equal(10, result.Value.Points[2].Remaining);
        Assert.Contains(result.Value.Warnings, warning => warning.Code == "burndown.remaining.empty");
    }

    [Fact]
    public void Campo_atual_vazio_preserva_horas_do_historico_antes_de_ser_limpo()
    {
        var result = Calculate(Snapshot(null, Start.AddDays(1)), Change(1, Start.AddHours(12), 6, null));

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal(6, result.Value.InitialRemaining);
        Assert.Equal(6, result.Value.Points[0].Remaining);
        Assert.Equal(0, result.Value.Points[1].Remaining);
        Assert.Equal(0, result.Value.Points[2].Remaining);
    }

    [Fact]
    public void Remaining_negativo_continua_invalido()
    {
        Assert.True(Calculate(Snapshot(-1, Start.AddDays(1))).IsFailure);
    }

    [Fact]
    public void Rejeita_campo_indisponivel_historico_incompleto_e_revisoes_inconsistentes()
    {
        var snapshot = Snapshot(6, Start.AddDays(1));
        Assert.True(Calculate(snapshot with { Fields = new(true, true, false) }).IsFailure);
        Assert.True(BurndownCalculator.Calculate(snapshot, new(Start.AddHours(1), snapshot.CollectedAt, snapshot.CollectedAt, []), Zone).IsFailure);
        Assert.True(Calculate(snapshot, Change(1, Start.AddHours(1), 10, 7)).IsFailure);
        Assert.True(Calculate(snapshot, Change(1, Start.AddHours(1), -1, 6)).IsFailure);
        Assert.True(Calculate(snapshot, Change(1, Start.AddHours(1), 10, 6) with { TaskId = 999 }).IsFailure);
    }

    [Fact]
    public void Rejeita_datas_invalidas_e_calendario_sem_dias_uteis()
    {
        var snapshot = Snapshot(6, Start.AddDays(1));
        Assert.True(Calculate(snapshot with { Sprint = snapshot.Sprint with { Start = null } }).IsFailure);
        Assert.True(Calculate(snapshot with { Calendar = new(new HashSet<DayOfWeek>(), []) }).IsFailure);
    }

    private static Result<Burndown> Calculate(SprintSnapshot snapshot, params EffortChange[] changes)
        => BurndownCalculator.Calculate(snapshot, new(Start, snapshot.CollectedAt, snapshot.CollectedAt, changes), Zone);

    private static EffortChange Change(int id, DateTimeOffset at, decimal? before, decimal? after)
        => new(1, "Task", "https://example.test/1", id, at, null, EffortField.Remaining, before, after);

    private static HashSet<DayOfWeek> WorkingDays() => [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday];
    private static SprintSnapshot Snapshot(decimal? remaining, DateTimeOffset collectedAt)
        => new(new("s1", "Sprint", "Projeto\\Sprint", new(2026, 9, 25), new(2026, 9, 29)),
            new(WorkingDays(), []), [],
            [new(1, "Task", null, "Active", "InProgress", "Projeto", "Projeto\\Sprint", 0, 12, remaining, "https://example.test/1")],
            new(true, true, true), collectedAt);
}
