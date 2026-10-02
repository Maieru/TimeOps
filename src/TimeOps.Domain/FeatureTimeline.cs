namespace TimeOps.Domain;

public sealed record FeatureTimelineRow(ParentWorkItem Feature, IReadOnlyList<TaskWork> Tasks)
{
    public bool HasValidDates => Feature.StartDate is { } start && Feature.EndDate is { } end && end >= start;
    public int? DurationDays => HasValidDates ? Feature.EndDate!.Value.DayNumber - Feature.StartDate!.Value.DayNumber + 1 : null;
}

public sealed record FeatureTimeline(
    IReadOnlyList<FeatureTimelineRow> Rows, DateOnly? Start, DateOnly? End, int TasksWithoutFeature);

public static class FeatureTimelineCalculator
{
    public static FeatureTimeline Calculate(IReadOnlyList<FeatureEffort> features, string? personId = null)
    {
        bool Matches(TaskWork task) => personId is null
            || string.Equals(task.Assignee?.Id, personId, StringComparison.OrdinalIgnoreCase);

        var rows = new List<FeatureTimelineRow>();
        var withoutFeature = new HashSet<int>();
        foreach (var feature in features)
        {
            var tasks = feature.Stories.SelectMany(story => story.Tasks).Where(Matches)
                .DistinctBy(task => task.Id).ToArray();
            if (feature.Feature is null)
            {
                foreach (var task in tasks) withoutFeature.Add(task.Id);
            }
            else if (tasks.Length > 0) rows.Add(new(feature.Feature, tasks));
        }

        var ordered = rows.OrderBy(row => !row.HasValidDates).ThenBy(row => row.Feature.StartDate)
            .ThenBy(row => row.Feature.Title, StringComparer.CurrentCultureIgnoreCase).ThenBy(row => row.Feature.Id).ToArray();
        var dated = ordered.Where(row => row.HasValidDates).ToArray();
        return new(ordered, dated.Length == 0 ? null : dated.Min(row => row.Feature.StartDate),
            dated.Length == 0 ? null : dated.Max(row => row.Feature.EndDate), withoutFeature.Count);
    }
}
