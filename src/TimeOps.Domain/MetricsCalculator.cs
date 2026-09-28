namespace TimeOps.Domain;

public static class MetricsCalculator
{
    public static Result<Dashboard> Calculate(SprintSnapshot snapshot, DateOnly referenceDate, DateOnly today)
    {
        if (referenceDate > today)
            return Result<Dashboard>.Failure(new("reference.future", ErrorCategory.Validation, "A data de referência não pode ser futura."));
        if (snapshot.Sprint.Start is null || snapshot.Sprint.End is null || snapshot.Sprint.End < snapshot.Sprint.Start)
            return Result<Dashboard>.Failure(new("sprint.dates", ErrorCategory.Incomplete, "A sprint precisa ter datas válidas de início e fim."));
        if (snapshot.Calendar.WorkingDays.Count == 0)
            return Result<Dashboard>.Failure(new("calendar.missing", ErrorCategory.Incomplete, "Os dias de trabalho da equipe não estão configurados."));
        var tasks = snapshot.Tasks.DistinctBy(task => task.Id).ToArray();
        var capacities = snapshot.Capacities.DistinctBy(member => member.Person.Id, StringComparer.OrdinalIgnoreCase).ToDictionary(member => member.Person.Id, StringComparer.OrdinalIgnoreCase);
        var people = new Dictionary<string, Person>(StringComparer.OrdinalIgnoreCase);
        foreach (var capacity in snapshot.Capacities) people[capacity.Person.Id] = capacity.Person;
        foreach (var task in tasks.Where(task => task.Assignee is not null)) people[task.Assignee!.Id] = task.Assignee!;

        var metrics = people.Values.Select(person =>
        {
            capacities.TryGetValue(person.Id, out var capacity);
            var assigned = tasks.Where(task => string.Equals(task.Assignee?.Id, person.Id, StringComparison.OrdinalIgnoreCase)).ToArray();
            return BuildPerson(person, capacity, assigned, snapshot, referenceDate);
        }).OrderBy(item => item.Person!.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();

        var unassignedTasks = tasks.Where(task => task.Assignee is null).ToArray();
        var unassigned = unassignedTasks.Length > 0 ? BuildPerson(null, null, unassignedTasks, snapshot, referenceDate) : null;
        var groups = unassigned is null ? metrics : metrics.Append(unassigned).ToArray();
        var warnings = groups.SelectMany(group => group.Warnings).Distinct().ToList();
        if (!snapshot.Fields.Completed) warnings.Add(new("field.completed.missing", "Completed não está disponível para Tasks neste processo; as métricas derivadas não podem ser calculadas."));
        if (!snapshot.Fields.Original) warnings.Add(new("field.original.missing", "Original Estimate não está disponível para Tasks neste processo."));
        if (!snapshot.Fields.Remaining) warnings.Add(new("field.remaining.missing", "Remaining Work não está disponível para Tasks neste processo."));
        if (snapshot.HierarchyError is not null) warnings.Add(new("hierarchy.unavailable", snapshot.HierarchyError));

        decimal? Aggregate(Func<PersonMetrics, decimal?> selector) => groups.All(group => selector(group).HasValue)
            ? groups.Sum(group => selector(group)!.Value) : null;
        decimal? AggregateCapacity(Func<PersonMetrics, decimal?> selector) => groups.Any(group => group.Person is null || !selector(group).HasValue)
            ? null : groups.Sum(group => selector(group)!.Value);

        var expected = AggregateCapacity(group => group.Expected);
        var totalCapacity = AggregateCapacity(group => group.TotalCapacity);
        var futureCapacity = AggregateCapacity(group => group.FutureCapacity);
        var completed = snapshot.Fields.Completed ? Aggregate(group => group.Completed) : null;
        var original = snapshot.Fields.Original ? Aggregate(group => group.Original) : null;
        var remaining = snapshot.Fields.Remaining ? Aggregate(group => group.Remaining) : null;
        var categoryCounts = tasks.GroupBy(task => task.StateCategory, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
        var done = tasks.Count(task => string.Equals(task.StateCategory, "Completed", StringComparison.OrdinalIgnoreCase));
        var team = new TeamMetrics(expected, totalCapacity, futureCapacity, completed, original, remaining,
            completed.HasValue && expected.HasValue ? completed - expected : null,
            expected > 0 && completed.HasValue ? completed / expected * 100 : null,
            futureCapacity.HasValue && remaining.HasValue ? futureCapacity - remaining : null,
            tasks.Length, done, tasks.Length > 0 ? (decimal)done / tasks.Length * 100 : null, categoryCounts);

        var features = snapshot.HierarchyError is null ? BuildFeatures(tasks, snapshot.Parents ?? [], snapshot.Fields.Completed) : null;
        return Result<Dashboard>.Success(new(snapshot.Sprint, referenceDate, snapshot.CollectedAt, metrics, unassigned, team, warnings, features, snapshot.HierarchyError));
    }

    private static IReadOnlyList<FeatureEffort> BuildFeatures(IReadOnlyList<TaskWork> tasks,
        IReadOnlyList<ParentWorkItem> parents, bool completedAvailable)
    {
        var byId = parents.DistinctBy(item => item.Id).ToDictionary(item => item.Id);
        var located = tasks.Select(task =>
        {
            byId.TryGetValue(task.ParentId ?? 0, out var parent);
            if (parent is not null && parent.Type.Equals("Feature", StringComparison.OrdinalIgnoreCase))
                return (Task: task, FeatureId: parent.Id, StoryId: 0);
            var featureId = parent?.ParentId is int id && byId.TryGetValue(id, out var feature)
                && feature.Type.Equals("Feature", StringComparison.OrdinalIgnoreCase) ? feature.Id : 0;
            return (Task: task, FeatureId: featureId, StoryId: parent?.Id ?? 0);
        });

        return located.GroupBy(item => item.FeatureId)
            .OrderBy(group => group.Key == 0).ThenBy(group => byId.GetValueOrDefault(group.Key)?.Title, StringComparer.CurrentCultureIgnoreCase)
            .Select(featureGroup =>
            {
                var stories = featureGroup.GroupBy(item => item.StoryId)
                    .OrderBy(group => group.Key == 0).ThenBy(group => byId.GetValueOrDefault(group.Key)?.Title, StringComparer.CurrentCultureIgnoreCase)
                    .Select(group =>
                    {
                        var storyTasks = group.Select(item => item.Task).OrderBy(task => task.Title, StringComparer.CurrentCultureIgnoreCase).ToArray();
                        return new StoryEffort(byId.GetValueOrDefault(group.Key), storyTasks,
                            completedAvailable ? storyTasks.Sum(task => task.Completed ?? 0) : null);
                    }).ToArray();
                return new FeatureEffort(byId.GetValueOrDefault(featureGroup.Key), stories,
                    completedAvailable ? stories.Sum(story => story.Completed ?? 0) : null);
            }).ToArray();
    }

    private static PersonMetrics BuildPerson(Person? person, MemberCapacity? capacity, IReadOnlyList<TaskWork> tasks,
        SprintSnapshot snapshot, DateOnly referenceDate)
    {
        var warnings = new List<DataWarning>();
        decimal? daily = capacity is null || capacity.ActivitiesPerDay.Count == 0 ? null : capacity.ActivitiesPerDay.Sum();
        if (daily is null)
            warnings.Add(new("capacity.missing", person is null ? "Há Tasks sem responsável e sem capacidade atribuível." : $"Capacidade ausente para {person.Name}."));
        if (snapshot.Fields.Completed && tasks.Any(task => task.Completed is null))
            warnings.Add(new("completed.empty", $"{tasks.Count(task => task.Completed is null)} Task(s) com Completed vazio em {(person?.Name ?? "sem responsável")}."));
        if (snapshot.Fields.Original && tasks.Any(task => task.Original is null))
            warnings.Add(new("original.empty", $"Há Tasks com Original Estimate vazio em {(person?.Name ?? "sem responsável")}."));
        if (snapshot.Fields.Remaining && tasks.Any(task => task.Remaining is null))
            warnings.Add(new("remaining.empty", $"Há Tasks com Remaining Work vazio em {(person?.Name ?? "sem responsável")}."));
        if (tasks.Any(task => task.Remaining > 0 && string.Equals(task.StateCategory, "Completed", StringComparison.OrdinalIgnoreCase)))
            warnings.Add(new("completed.remaining", $"Há Tasks concluídas com trabalho restante em {(person?.Name ?? "sem responsável")}."));

        var days = EligibleDays(snapshot, capacity).ToArray();
        var elapsed = days.Count(day => day <= referenceDate);
        var future = days.Count(day => day > referenceDate);
        var expected = daily * elapsed;
        var total = daily * days.Length;
        var futureCapacity = daily * future;
        decimal? completed = snapshot.Fields.Completed ? tasks.Sum(task => task.Completed ?? 0) : null;
        decimal? original = snapshot.Fields.Original ? tasks.Sum(task => task.Original ?? 0) : null;
        decimal? remaining = snapshot.Fields.Remaining ? tasks.Sum(task => task.Remaining ?? 0) : null;

        return new(person, daily, elapsed, days.Length, future, expected, total, futureCapacity,
            completed, original, remaining, expected.HasValue && completed.HasValue ? completed - expected : null,
            expected > 0 && completed.HasValue ? completed / expected * 100 : null,
            remaining.HasValue && futureCapacity.HasValue ? futureCapacity - remaining : null, tasks, warnings);
    }

    private static IEnumerable<DateOnly> EligibleDays(SprintSnapshot snapshot, MemberCapacity? capacity)
    {
        if (capacity is null) yield break;
        for (var day = snapshot.Sprint.Start!.Value; day <= snapshot.Sprint.End!.Value; day = day.AddDays(1))
        {
            if (snapshot.Calendar.WorkingDays.Contains(day.DayOfWeek)
                && !snapshot.Calendar.DaysOff.Any(range => range.Contains(day))
                && !capacity.DaysOff.Any(range => range.Contains(day)))
                yield return day;
        }
    }
}
