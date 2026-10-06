using TimeOps.Domain;

namespace TimeOps.Application;

public sealed class TaskExportService(IDevOpsGateway gateway, TimeProvider clock, string timeZoneId)
{
    public async Task<Result<TaskExportData>> LoadAsync(string projectId, string teamId, Sprint sprint,
        DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(projectId) || string.IsNullOrWhiteSpace(teamId) || string.IsNullOrWhiteSpace(sprint.Id))
            return Fail("context.invalid", ErrorCategory.Validation, "Selecione projeto, equipe e sprint.");
        TimeZoneInfo zone;
        try { zone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId); }
        catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException)
        { return Fail("timezone.invalid", ErrorCategory.Validation, "O fuso configurado não é válido."); }
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), zone).DateTime);
        if (to < from)
            return Fail("export.dates", ErrorCategory.Validation, "A data final deve ser igual ou posterior à data inicial.");
        if (from > today || to > today)
            return Fail("export.future", ErrorCategory.Validation, "As datas da exportação não podem ser futuras.");

        var snapshotResult = await gateway.LoadSnapshotAsync(projectId, teamId, sprint, true, cancellationToken);
        if (snapshotResult.IsFailure) return Result<TaskExportData>.Failure(snapshotResult.Error!);
        var snapshot = snapshotResult.Value;
        if (!snapshot.Fields.Completed)
            return Fail("field.completed.missing", ErrorCategory.Incomplete, "Completed Work não está disponível para Tasks neste processo.");
        if (snapshot.HierarchyError is not null || snapshot.Parents is null)
            return Fail("hierarchy.unavailable", ErrorCategory.Incomplete,
                "Não foi possível consultar as Features das tarefas. Atualize os dados e tente novamente.");

        var start = StartOfDay(from, zone);
        var end = StartOfDay(to.AddDays(1), zone).AddTicks(-1);
        if (end > snapshot.CollectedAt) end = snapshot.CollectedAt;
        if (end < start) return Result<TaskExportData>.Success(new(sprint, from, to, snapshot.CollectedAt, []));
        var historyResult = await gateway.LoadCompletedHistoryAsync(projectId, teamId, snapshot, start, end, cancellationToken);
        if (historyResult.IsFailure) return Result<TaskExportData>.Failure(historyResult.Error!);
        var tasks = snapshot.Tasks.DistinctBy(task => task.Id).ToDictionary(task => task.Id);
        var parents = snapshot.Parents.DistinctBy(parent => parent.Id).ToDictionary(parent => parent.Id);
        var rows = new List<TaskExportRow>();
        foreach (var change in historyResult.Value.Changes)
        {
            if (change.Field != EffortField.Completed || change.Delta <= 0 || change.ChangedAt < start || change.ChangedAt > end) continue;
            if (!tasks.TryGetValue(change.TaskId, out var task))
                return Fail("export.scope", ErrorCategory.Incomplete, "Uma tarefa mudou durante a consulta. Carregue as alterações novamente.");
            parents.TryGetValue(task.ParentId ?? 0, out var parent);
            var feature = parent?.Type.Equals("Feature", StringComparison.OrdinalIgnoreCase) == true ? parent
                : parent?.ParentId is int id && parents.TryGetValue(id, out var ancestor)
                    && ancestor.Type.Equals("Feature", StringComparison.OrdinalIgnoreCase) ? ancestor : null;
            rows.Add(new(task.Id, change.UpdateId, change.ChangedBy, task.Title, feature?.Title,
                change.ChangedAt, DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(change.ChangedAt, zone).DateTime), change.Delta));
        }
        var ordered = rows.OrderBy(row => row.AuthorName, StringComparer.Create(System.Globalization.CultureInfo.GetCultureInfo("pt-BR"), true))
            .ThenBy(row => row.ChangedAt).ThenBy(row => row.TaskId).ThenBy(row => row.UpdateId).ToArray();
        return Result<TaskExportData>.Success(new(sprint, from, to, snapshot.CollectedAt, ordered));
    }

    private static DateTimeOffset StartOfDay(DateOnly day, TimeZoneInfo zone)
    {
        var local = day.ToDateTime(TimeOnly.MinValue);
        // Some historical Brazilian DST transitions skip midnight.
        while (zone.IsInvalidTime(local)) local = local.AddMinutes(1);
        return new(local, zone.GetUtcOffset(local));
    }

    public static string FileName(TaskExportData data)
    {
        var name = new string(data.Sprint.Name.Select(character => char.IsControl(character)
            || "<>:\"/\\|?*".Contains(character) ? '_' : character).ToArray()).Trim().Trim('.');
        if (name.Length == 0) name = "Sprint";
        if (name.Length > 100) name = name[..100];
        return $"TimeOps_{name}_{data.From:yyyy-MM-dd}_{data.To:yyyy-MM-dd}.xlsx";
    }

    private static Result<TaskExportData> Fail(string code, ErrorCategory category, string message)
        => Result<TaskExportData>.Failure(new(code, category, message));
}
