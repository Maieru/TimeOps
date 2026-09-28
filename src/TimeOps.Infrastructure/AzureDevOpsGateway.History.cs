using System.Globalization;
using System.Text.Json;
using TimeOps.Domain;

namespace TimeOps.Infrastructure;

public sealed partial class AzureDevOpsGateway
{
    private const int HistoryPageSize = 200;
    private const int MaxChangedTasks = 1000;
    private static readonly (string Name, EffortField Field)[] HistoryFields =
    [
        ("Microsoft.VSTS.Scheduling.CompletedWork", EffortField.Completed),
        ("Microsoft.VSTS.Scheduling.OriginalEstimate", EffortField.Original),
        ("Microsoft.VSTS.Scheduling.RemainingWork", EffortField.Remaining)
    ];

    public async Task<Result<EffortHistory>> LoadEffortHistoryAsync(string projectId, string teamId, Sprint sprint,
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        var config = ValidateConfig();
        if (config.IsFailure) return Result<EffortHistory>.Failure(config.Error!);
        if (to <= from) return Result<EffortHistory>.Failure(new("history.window", ErrorCategory.Validation, "O período do histórico é inválido."));

        var areasResponse = await SendAsync(HttpMethod.Get,
            $"{TeamPath(projectId, teamId)}/_apis/work/teamsettings/teamfieldvalues?{Version}", null, cancellationToken);
        if (areasResponse.IsFailure) return Result<EffortHistory>.Failure(areasResponse.Error!);
        var areas = ParseAreas(areasResponse.Value.Data);
        if (areas.IsFailure) return Result<EffortHistory>.Failure(areas.Error!);

        var areaClause = string.Join(" OR ", areas.Value.Select(area => area.IncludeChildren
            ? $"[System.AreaPath] UNDER '{Wiql(area.Path)}'"
            : $"[System.AreaPath] = '{Wiql(area.Path)}'"));
        var cutoff = from.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ", CultureInfo.InvariantCulture);
        var query = $"SELECT [System.Id] FROM WorkItems WHERE [System.TeamProject] = @project AND [System.WorkItemType] = 'Task' AND [System.IterationPath] = '{Wiql(sprint.Path)}' AND ({areaClause}) AND [System.ChangedDate] >= '{cutoff}'";
        var wiql = await SendAsync(HttpMethod.Post,
            $"{OrgPath}/{Segment(projectId)}/_apis/wit/wiql?timePrecision=true&$top=20000&{Version}", new WiqlRequest(query), cancellationToken);
        if (wiql.IsFailure) return Result<EffortHistory>.Failure(wiql.Error!);
        if (!TryArray(wiql.Value.Data, "workItems", out var found))
            return Incomplete<EffortHistory>("A consulta do histórico retornou dados incompletos.");

        var ids = new HashSet<int>();
        foreach (var item in found.EnumerateArray())
        {
            var id = Int(item, "id");
            if (id is null) return Incomplete<EffortHistory>("A consulta do histórico retornou uma Task sem ID.");
            ids.Add(id.Value);
        }
        if (found.GetArrayLength() >= 20000 || ids.Count > MaxChangedTasks)
            return Incomplete<EffortHistory>("Mais de 1.000 Tasks mudaram neste período. O histórico não foi exibido como se estivesse completo.");
        if (ids.Count == 0) return Result<EffortHistory>.Success(new(from, to, clock.GetUtcNow(), []));

        var tasks = await LoadHistoryTasksAsync(projectId, sprint, areas.Value, ids, cancellationToken);
        if (tasks.IsFailure) return Result<EffortHistory>.Failure(tasks.Error!);

        var changes = new List<EffortChange>();
        foreach (var group in tasks.Value.Chunk(4))
        {
            var results = await Task.WhenAll(group.Select(task => LoadTaskUpdatesAsync(projectId, task, from, to, cancellationToken)));
            foreach (var result in results)
            {
                if (result.IsFailure) return Result<EffortHistory>.Failure(result.Error!);
                changes.AddRange(result.Value);
            }
        }

        return Result<EffortHistory>.Success(new(from, to, clock.GetUtcNow(), changes
            .OrderByDescending(change => change.ChangedAt)
            .ThenByDescending(change => change.UpdateId)
            .ThenBy(change => change.TaskId)
            .ToArray()));
    }

    private async Task<Result<IReadOnlyList<HistoryTask>>> LoadHistoryTasksAsync(string projectId, Sprint sprint,
        IReadOnlyList<AreaRule> areas, HashSet<int> ids, CancellationToken cancellationToken)
    {
        var tasks = new List<HistoryTask>();
        foreach (var batch in ids.Order().Chunk(200))
        {
            var response = await SendAsync(HttpMethod.Post, $"{OrgPath}/{Segment(projectId)}/_apis/wit/workitemsbatch?{Version}",
                new WorkItemsRequest(batch, ["System.Title", "System.WorkItemType", "System.AreaPath", "System.IterationPath"]), cancellationToken);
            if (response.IsFailure) return Result<IReadOnlyList<HistoryTask>>.Failure(response.Error!);
            if (!TryArray(response.Value.Data, "value", out var values) || values.GetArrayLength() != batch.Length)
                return Incomplete<IReadOnlyList<HistoryTask>>("Nem todas as Tasks do histórico puderam ser lidas.");
            foreach (var item in values.EnumerateArray())
            {
                var id = Int(item, "id");
                var fields = Property(item, "fields");
                var title = String(fields, "System.Title");
                var type = String(fields, "System.WorkItemType");
                var area = String(fields, "System.AreaPath");
                var iteration = String(fields, "System.IterationPath");
                if (id is null || !ids.Contains(id.Value) || title is null || !string.Equals(type, "Task", StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(iteration, sprint.Path, StringComparison.OrdinalIgnoreCase)
                    || area is null || !areas.Any(rule => rule.Matches(area)))
                    return Incomplete<IReadOnlyList<HistoryTask>>("Uma Task mudou de tipo, área ou sprint durante a consulta do histórico. Atualize novamente.");
                tasks.Add(new(id.Value, title, $"https://dev.azure.com/{Segment(connection.Organization)}/{Segment(projectId)}/_workitems/edit/{id.Value}"));
            }
        }
        return tasks.Select(task => task.Id).Distinct().Count() == ids.Count
            ? Result<IReadOnlyList<HistoryTask>>.Success(tasks)
            : Incomplete<IReadOnlyList<HistoryTask>>("As Tasks do histórico mudaram durante a coleta. Atualize novamente.");
    }

    private async Task<Result<IReadOnlyList<EffortChange>>> LoadTaskUpdatesAsync(string projectId, HistoryTask task,
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        var changes = new List<EffortChange>();
        var seen = new HashSet<int>();
        for (var skip = 0; ; skip += HistoryPageSize)
        {
            var response = await SendAsync(HttpMethod.Get,
                $"{OrgPath}/{Segment(projectId)}/_apis/wit/workItems/{task.Id}/updates?$top={HistoryPageSize}&$skip={skip}&{Version}", null, cancellationToken);
            if (response.IsFailure) return Result<IReadOnlyList<EffortChange>>.Failure(response.Error!);
            if (!TryArray(response.Value.Data, "value", out var updates) || updates.GetArrayLength() > HistoryPageSize)
                return Incomplete<IReadOnlyList<EffortChange>>("O histórico de uma Task retornou uma página inválida.");

            foreach (var update in updates.EnumerateArray())
            {
                var updateId = Int(update, "id");
                var workItemId = Int(update, "workItemId");
                if (updateId is null || workItemId != task.Id || !seen.Add(updateId.Value))
                    return Incomplete<IReadOnlyList<EffortChange>>("Uma atualização de Task retornou dados incompletos ou repetidos.");

                var fields = Property(update, "fields");
                if (!HistoryFields.Any(field => Property(fields, field.Name).ValueKind != JsonValueKind.Undefined)) continue;
                var dateText = String(Property(fields, "System.ChangedDate"), "newValue")
                    ?? String(Property(fields, "System.AuthorizedDate"), "newValue");
                if (dateText is null || !DateTimeOffset.TryParse(dateText, CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal, out var changedAt))
                    return Incomplete<IReadOnlyList<EffortChange>>("Uma alteração de horas não trouxe a data em que a Task foi modificada.");
                if (changedAt < from || changedAt > to) continue;

                var revisedBy = Property(update, "revisedBy");
                var actorName = String(revisedBy, "displayName");
                var actorId = IdentityId(revisedBy);
                var actor = actorName is null ? null : new Person(actorId ?? actorName, actorName);
                foreach (var (fieldName, fieldKind) in HistoryFields)
                {
                    var field = Property(fields, fieldName);
                    if (field.ValueKind == JsonValueKind.Undefined) continue;
                    if (field.ValueKind != JsonValueKind.Object
                        || !TryHours(Property(field, "oldValue"), out var before)
                        || !TryHours(Property(field, "newValue"), out var after))
                        return Incomplete<IReadOnlyList<EffortChange>>("Uma alteração de horas retornou valores inválidos.");
                    if (before != after)
                        changes.Add(new(task.Id, task.Title, task.Url, updateId.Value, changedAt, actor, fieldKind, before, after));
                }
            }

            if (updates.GetArrayLength() < HistoryPageSize) break;
            if (skip >= 10000)
                return Incomplete<IReadOnlyList<EffortChange>>("Uma Task excedeu o limite de atualizações para esta consulta. O histórico está indisponível.");
        }
        return Result<IReadOnlyList<EffortChange>>.Success(changes);
    }

    private static bool TryHours(JsonElement value, out decimal? hours)
    {
        hours = null;
        if (value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null) return true;
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDecimal(out var number)) return false;
        hours = number;
        return true;
    }

    private sealed record HistoryTask(int Id, string Title, string Url);
}
