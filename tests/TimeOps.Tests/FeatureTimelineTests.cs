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

    private static FeatureEffort Feature(int id, DateOnly? start, DateOnly? end, params TaskWork[] tasks)
        => new(new(id, "Feature " + id, "Feature", null, "https://example.com/" + id, start, end),
            tasks.Select(task => new StoryEffort(null, [task], 0)).ToArray(), 0);
    private static TaskWork Task(int id, Person? person)
        => new(id, "Task " + id, person, "Active", "InProgress", "Area", "Sprint", 0, 0, 0, "https://example.com/" + id);
}
