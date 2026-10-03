using TimeOps.Domain;

namespace TimeOps.Tests;

public sealed class FeatureTimelineTests
{
    private static readonly Person Ana = new("ana", "Ana");
    private static readonly Person OtherAna = new("other-ana", "Ana");

    [Fact]
    public void Filtra_por_identidade_com_uma_task_em_qualquer_historia_e_preserva_periodo_completo()
    {
        var first = Feature(10, new(2026, 8, 1), new(2026, 11, 30), Task(1, OtherAna), Task(2, Ana));
        var other = Feature(20, new(2026, 9, 1), new(2026, 9, 10), Task(3, OtherAna));

        var result = FeatureTimelineCalculator.Calculate([first, other], "ANA");

        var row = Assert.Single(result.Rows);
        Assert.Equal(10, row.Feature.Id);
        Assert.Equal(2, Assert.Single(row.Tasks).Id);
        Assert.Equal(new DateOnly(2026, 8, 1), result.Start);
        Assert.Equal(new DateOnly(2026, 11, 30), result.End);
        Assert.Equal(122, row.DurationDays);
    }

    [Fact]
    public void Mantem_datas_ausentes_e_invertidas_sem_inventar_barras_ou_alterar_eixo()
    {
        var valid = Feature(1, new(2026, 9, 2), new(2026, 9, 2), Task(1, Ana));
        var missing = Feature(2, null, new(2030, 1, 1), Task(2, Ana));
        var inverted = Feature(3, new(2020, 1, 2), new(2020, 1, 1), Task(3, Ana));
        var noEnd = Feature(4, new(2026, 8, 1), null, Task(4, Ana));

        var result = FeatureTimelineCalculator.Calculate([missing, inverted, noEnd, valid]);

        Assert.Equal(4, result.Rows.Count);
        Assert.Equal(1, result.Rows[0].DurationDays);
        Assert.All(result.Rows.Skip(1), row => { Assert.False(row.HasValidDates); Assert.Null(row.DurationDays); });
        Assert.Equal(new DateOnly(2026, 9, 2), result.Start);
        Assert.Equal(result.Start, result.End);
    }

    [Fact]
    public void Sem_periodos_validos_eixo_fica_ausente()
    {
        var result = FeatureTimelineCalculator.Calculate([Feature(1, null, null, Task(1, Ana))]);
        Assert.Single(result.Rows);
        Assert.Null(result.Start);
        Assert.Null(result.End);
    }

    [Fact]
    public void Toda_equipe_inclui_tasks_sem_responsavel_e_deduplica_tasks_sem_feature()
    {
        var orphan = Task(2, Ana);
        var without = new FeatureEffort(null, [new(null, [orphan, orphan, Task(3, OtherAna)], 0)], 0);
        var feature = Feature(1, new(2026, 9, 1), new(2026, 9, 30), Task(1, null));

        var all = FeatureTimelineCalculator.Calculate([feature, without]);
        Assert.Single(all.Rows);
        Assert.Equal(2, all.TasksWithoutFeature);

        var ana = FeatureTimelineCalculator.Calculate([feature, without], Ana.Id);
        Assert.Empty(ana.Rows);
        Assert.Equal(1, ana.TasksWithoutFeature);

        var noTasks = FeatureTimelineCalculator.Calculate([feature, without], "nobody");
        Assert.Empty(noTasks.Rows);
        Assert.Equal(0, noTasks.TasksWithoutFeature);
        Assert.Null(noTasks.Start);
    }

    [Fact]
    public void Hierarquia_resolve_tasks_diretamente_na_feature_e_via_historia()
    {
        var tasks = new[] { Task(1, Ana) with { ParentId = 10 }, Task(2, OtherAna) with { ParentId = 20 } };
        var snapshot = new SprintSnapshot(new("s", "Sprint", "Sprint", new(2026, 9, 1), new(2026, 9, 14)),
            new(new HashSet<DayOfWeek> { DayOfWeek.Monday }, []), [], tasks, new(true, true, true), DateTimeOffset.UtcNow,
            [new(10, "Story", "User Story", 20, "https://example.com/10"),
             new(20, "Feature", "Feature", null, "https://example.com/20", new(2026, 8, 1), new(2026, 10, 1))]);

        var dashboard = MetricsCalculator.Calculate(snapshot, new(2026, 9, 10), new(2026, 9, 10)).Value;
        var all = FeatureTimelineCalculator.Calculate(dashboard.Features!);
        Assert.Equal(2, Assert.Single(all.Rows).Tasks.Count);
        var ana = FeatureTimelineCalculator.Calculate(dashboard.Features!, Ana.Id);
        Assert.Equal(1, Assert.Single(Assert.Single(ana.Rows).Tasks).Id);
        Assert.Equal(new DateOnly(2026, 10, 1), ana.End);
    }

    [Fact]
    public void Task_repetida_em_historias_da_feature_aparece_uma_vez()
    {
        var task = Task(1, Ana);
        var feature = Feature(10, new(2026, 9, 1), new(2026, 9, 30), task, task, Task(2, OtherAna));

        var all = FeatureTimelineCalculator.Calculate([feature]);
        Assert.Equal(new[] { 1, 2 }, Assert.Single(all.Rows).Tasks.Select(item => item.Id));
        var filtered = FeatureTimelineCalculator.Calculate([feature], Ana.Id);
        Assert.Equal(1, Assert.Single(Assert.Single(filtered.Rows).Tasks).Id);
    }

    [Fact]
    public void Eixo_usa_extremos_de_todas_as_features_e_recalcula_ao_filtrar()
    {
        var longFeature = Feature(10, new(2026, 8, 1), new(2026, 12, 31), Task(1, OtherAna));
        var anaFeature = Feature(20, new(2026, 9, 1), new(2026, 9, 30), Task(2, Ana));
        var empty = Feature(30, new(2000, 1, 1), new(2030, 1, 1));

        var all = FeatureTimelineCalculator.Calculate([anaFeature, longFeature, empty]);
        Assert.Equal(2, all.Rows.Count);
        Assert.Equal(new DateOnly(2026, 8, 1), all.Start);
        Assert.Equal(new DateOnly(2026, 12, 31), all.End);

        var filtered = FeatureTimelineCalculator.Calculate([anaFeature, longFeature, empty], Ana.Id);
        Assert.Equal(new DateOnly(2026, 9, 1), filtered.Start);
        Assert.Equal(new DateOnly(2026, 9, 30), filtered.End);
    }

    [Fact]
    public void Ordenacao_desempata_data_por_titulo_e_id_e_deixa_periodos_invalidos_no_fim()
    {
        var beta = Feature(1, new(2026, 9, 1), new(2026, 9, 30), Task(1, Ana));
        var alpha = Feature(2, new(2026, 9, 1), new(2026, 9, 30), Task(2, Ana)) with
        { Feature = beta.Feature! with { Id = 2, Title = "Alpha" } };
        beta = beta with { Feature = beta.Feature! with { Title = "Beta" } };
        var sameTitle = alpha with { Feature = alpha.Feature! with { Id = 3, Title = "alpha" } };
        var earlier = Feature(4, new(2026, 8, 1), new(2026, 8, 31), Task(4, Ana));
        var invalid = Feature(5, new(2020, 1, 2), new(2020, 1, 1), Task(5, Ana));

        var result = FeatureTimelineCalculator.Calculate([invalid, beta, sameTitle, alpha, earlier]);

        Assert.Equal(new[] { 4, 2, 3, 1, 5 }, result.Rows.Select(row => row.Feature.Id));
    }

    [Fact]
    public void Duracao_inclusiva_conta_dia_bissexto()
    {
        var result = FeatureTimelineCalculator.Calculate(
            [Feature(1, new(2024, 2, 28), new(2024, 3, 1), Task(1, Ana))]);

        Assert.Equal(3, Assert.Single(result.Rows).DurationDays);
    }

    private static FeatureEffort Feature(int id, DateOnly? start, DateOnly? end, params TaskWork[] tasks)
        => new(new(id, "Feature " + id, "Feature", null, "https://example.com/" + id, start, end),
            tasks.Select(task => new StoryEffort(null, [task], 0)).ToArray(), 0);
    private static TaskWork Task(int id, Person? person)
        => new(id, "Task " + id, person, "Active", "InProgress", "Area", "Sprint", 0, 0, 0, "https://example.com/" + id);
}
