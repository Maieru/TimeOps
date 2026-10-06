using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
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

        return await ReadHistoryAsync(projectId, tasks.Value, from, to, cancellationToken);
    }

    public async Task<Result<EffortHistory>> LoadBurndownHistoryAsync(string projectId, string teamId, SprintSnapshot snapshot,
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        var config = ValidateConfig();
        if (config.IsFailure) return Result<EffortHistory>.Failure(config.Error!);
        if (to <= from) return Result<EffortHistory>.Failure(new("history.window", ErrorCategory.Validation, "O período do histórico é inválido."));
        cancellationToken.ThrowIfCancellationRequested();
        // The snapshot already contains the verified scope, revisions and current values.
        // A refreshed snapshot or a new connection gets a separate entry, even with the same clock time.
        var key = ("burndown-history", connection.CachePartition, projectId, teamId, snapshot, from, to);
        if (cache.TryGetValue(key, out EffortHistory? cached) && cached is not null)
            return Result<EffortHistory>.Success(cached);
        var tasks = snapshot.Tasks.Where(task => task.ChangedAt is null || task.ChangedAt >= from)
            .Select(task => new HistoryTask(task.Id, task.Title, task.Url, task.Revision)).ToArray();
        if (tasks.Length > MaxChangedTasks)
            return Incomplete<EffortHistory>("Mais de 1.000 Tasks mudaram neste período. O histórico não foi exibido como se estivesse completo.");
        var result = await ReadHistoryAsync(projectId, tasks, from, to, cancellationToken, onlyField: EffortField.Remaining);
        if (result.IsSuccess) cache.Set(key, result.Value, TimeSpan.FromMinutes(5));
        return result;
    }

    public async Task<Result<EffortHistory>> LoadCompletedHistoryAsync(string projectId, string teamId, SprintSnapshot snapshot,
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        var config = ValidateConfig();
        if (config.IsFailure) return Result<EffortHistory>.Failure(config.Error!);
        if (to < from || to > snapshot.CollectedAt)
            return Result<EffortHistory>.Failure(new("history.window", ErrorCategory.Validation, "O período da exportação é inválido."));
        var tasks = snapshot.Tasks.DistinctBy(task => task.Id)
            .Where(task => task.ChangedAt is null || task.ChangedAt >= from)
            .Select(task => new HistoryTask(task.Id, task.Title, task.Url, task.Revision)).ToArray();
        if (tasks.Length > MaxChangedTasks)
            return Incomplete<EffortHistory>("Mais de 1.000 Tasks mudaram neste período. Reduza o intervalo da exportação.");
        return await ReadHistoryAsync(projectId, tasks, from, to, cancellationToken, onlyField: EffortField.Completed);
    }

    private async Task<Result<EffortHistory>> ReadHistoryAsync(string projectId, IReadOnlyList<HistoryTask> tasks,
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken, EffortField? onlyField = null)
    {
        var results = new Result<IReadOnlyList<EffortChange>>[tasks.Count];
        Error? failure = null;
        await Parallel.ForEachAsync(Enumerable.Range(0, tasks.Count), new ParallelOptions
        {
            MaxDegreeOfParallelism = 8,
            CancellationToken = cancellationToken
        }, async (index, token) =>
        {
            if (Volatile.Read(ref failure) is not null) return;
            results[index] = await LoadTaskUpdatesAsync(projectId, tasks[index], from, to, token, onlyField);
            if (results[index].IsFailure) Interlocked.CompareExchange(ref failure, results[index].Error!, null);
        });
        if (failure is not null) return Result<EffortHistory>.Failure(failure);
        var changes = new List<EffortChange>();
        foreach (var result in results)
        {
            changes.AddRange(result.Value);
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
                tasks.Add(new(id.Value, title, $"https://dev.azure.com/{Segment(connection.Organization)}/{Segment(projectId)}/_workitems/edit/{id.Value}", Int(item, "rev")));
            }
        }
        return tasks.Select(task => task.Id).Distinct().Count() == ids.Count
            ? Result<IReadOnlyList<HistoryTask>>.Success(tasks)
            : Incomplete<IReadOnlyList<HistoryTask>>("As Tasks do histórico mudaram durante a coleta. Atualize novamente.");
    }

    private async Task<Result<IReadOnlyList<EffortChange>>> LoadTaskUpdatesAsync(string projectId, HistoryTask task,
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken, EffortField? onlyField = null)
    {
        var updates = await LoadEffortUpdatesAsync(projectId, task, cancellationToken);
        if (updates.IsFailure) return Result<IReadOnlyList<EffortChange>>.Failure(updates.Error!);
        var changes = new List<EffortChange>();
        foreach (var update in updates.Value)
        {
            var fields = Property(update, "fields");
            if (onlyField is { } selected && Property(fields, HistoryFields.Single(field => field.Field == selected).Name).ValueKind == JsonValueKind.Undefined) continue;
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
                if (onlyField is not null && fieldKind != onlyField) continue;
                var field = Property(fields, fieldName);
                if (field.ValueKind == JsonValueKind.Undefined) continue;
                if (field.ValueKind != JsonValueKind.Object
                    || !TryHours(Property(field, "oldValue"), out var before)
                    || !TryHours(Property(field, "newValue"), out var after))
                    return Incomplete<IReadOnlyList<EffortChange>>("Uma alteração de horas retornou valores inválidos.");
                if (before != after)
                    changes.Add(new(task.Id, task.Title, task.Url, Int(update, "id")!.Value, changedAt, actor, fieldKind, before, after));
            }
        }
        return Result<IReadOnlyList<EffortChange>>.Success(changes);
    }

    private async Task<Result<IReadOnlyList<JsonElement>>> LoadEffortUpdatesAsync(string projectId, HistoryTask task,
        CancellationToken cancellationToken)
    {
        // Check the current revision in the batch before reusing updates, including across time windows.
        var key = $"effort-updates:{connection.CachePartition}:{projectId}:{task.Id}:{task.Revision}";
        if (task.Revision is > 0 && cache.TryGetValue(key, out IReadOnlyList<JsonElement>? cached) && cached is not null)
            return Result<IReadOnlyList<JsonElement>>.Success(cached);
        var effortUpdates = new List<JsonElement>();
        var seen = new HashSet<int>();
        for (var skip = 0; ; skip += HistoryPageSize)
        {
            var response = await SendAsync(HttpMethod.Get,
                $"{OrgPath}/{Segment(projectId)}/_apis/wit/workItems/{task.Id}/updates?$top={HistoryPageSize}&$skip={skip}&{Version}", null, cancellationToken);
            if (response.IsFailure) return Result<IReadOnlyList<JsonElement>>.Failure(response.Error!);
            if (!TryArray(response.Value.Data, "value", out var updates) || updates.GetArrayLength() > HistoryPageSize)
                return Incomplete<IReadOnlyList<JsonElement>>("O histórico de uma Task retornou uma página inválida.");

            foreach (var update in updates.EnumerateArray())
            {
                var updateId = Int(update, "id");
                var workItemId = Int(update, "workItemId");
                if (updateId is null || workItemId != task.Id || !seen.Add(updateId.Value))
                    return Incomplete<IReadOnlyList<JsonElement>>("Uma atualização de Task retornou dados incompletos ou repetidos.");

                var fields = Property(update, "fields");
                if (!HistoryFields.Any(field => Property(fields, field.Name).ValueKind != JsonValueKind.Undefined)) continue;
                effortUpdates.Add(update.Clone());
            }

            if (updates.GetArrayLength() < HistoryPageSize) break;
            if (skip >= 10000)
                return Incomplete<IReadOnlyList<JsonElement>>("Uma Task excedeu o limite de atualizações para esta consulta. O histórico está indisponível.");
        }
        if (task.Revision is > 0) cache.Set(key, (IReadOnlyList<JsonElement>)effortUpdates, TimeSpan.FromMinutes(5));
        return Result<IReadOnlyList<JsonElement>>.Success(effortUpdates);
    }

    private static bool TryHours(JsonElement value, out decimal? hours)
    {
        hours = null;
        if (value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null) return true;
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDecimal(out var number)) return false;
        hours = number;
        return true;
    }

    private sealed record HistoryTask(int Id, string Title, string Url, int? Revision);
}
