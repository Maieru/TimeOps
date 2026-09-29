namespace TimeOps.Domain;

public sealed record BurndownPoint(DateOnly Date, decimal Ideal, decimal? Remaining, bool IsBaseline = false);
public sealed record Burndown(Sprint Sprint, DateTimeOffset CollectedAt, decimal InitialRemaining,
    int TaskCount, IReadOnlyList<BurndownPoint> Points, IReadOnlyList<DataWarning> Warnings);

public static class BurndownCalculator
{
    public static Result<Burndown> Calculate(SprintSnapshot snapshot, EffortHistory history, TimeZoneInfo timeZone)
    {
        if (snapshot.Sprint.Start is not { } start || snapshot.Sprint.End is not { } end || end < start)
            return Fail("sprint.dates", "A sprint precisa ter datas válidas para exibir o burndown.");
        if (!snapshot.Fields.Remaining)
            return Fail("burndown.remaining", "Remaining Work está indisponível neste processo. Confira o campo no Azure DevOps e atualize para ver o burndown.");
        if (snapshot.Tasks.Any(task => task.Remaining < 0))
            return Fail("burndown.negative", "Uma Task tem Remaining Work negativo. Corrija o campo e atualize.");

        var midnight = start.ToDateTime(TimeOnly.MinValue);
        var startTime = new DateTimeOffset(midnight, timeZone.GetUtcOffset(midnight));
        var collectionDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(snapshot.CollectedAt, timeZone).DateTime);
        var hasStarted = snapshot.CollectedAt >= startTime;
        if (hasStarted && (history.From > startTime || history.To < snapshot.CollectedAt))
            return Fail("burndown.history", "O histórico não cobre toda a iteração. Atualize para tentar novamente.");

        var changes = history.Changes.Where(change => change.Field == EffortField.Remaining
                && change.ChangedAt >= startTime && change.ChangedAt <= snapshot.CollectedAt)
            .OrderBy(change => change.ChangedAt).ThenBy(change => change.UpdateId).ToArray();
        var values = snapshot.Tasks.ToDictionary(task => task.Id, task => task.Remaining ?? 0);
        foreach (var change in changes.Reverse())
        {
            if (!values.TryGetValue(change.TaskId, out var current) || current != (change.After ?? 0)
                || change.Before < 0 || change.After < 0)
                return Fail("burndown.changed", "As Tasks mudaram durante a coleta ou o histórico está inconsistente. Atualize para tentar novamente.");
            values[change.TaskId] = change.Before ?? 0;
        }

        var initial = values.Values.Sum();
        var days = Enumerable.Range(0, end.DayNumber - start.DayNumber + 1).Select(start.AddDays).ToArray();
        bool Working(DateOnly day) => snapshot.Calendar.WorkingDays.Contains(day.DayOfWeek)
            && !snapshot.Calendar.DaysOff.Any(range => range.Contains(day));
        var workingDays = days.Count(Working);
        if (workingDays == 0)
            return Fail("burndown.calendar", "A iteração não tem dias úteis no calendário da equipe. Confira o calendário no Azure DevOps.");

        var points = new List<BurndownPoint> { new(start, initial, hasStarted ? initial : null, true) };
        var elapsedWorkingDays = 0;
        var remaining = initial;
        var changeIndex = 0;
        foreach (var day in days)
        {
            if (Working(day)) elapsedWorkingDays++;
            while (changeIndex < changes.Length
                && DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(changes[changeIndex].ChangedAt, timeZone).DateTime) <= day)
                remaining += changes[changeIndex++].Delta;
            points.Add(new(day, initial * (workingDays - elapsedWorkingDays) / workingDays,
                hasStarted && day <= collectionDate ? remaining : null));
        }

        var warnings = new List<DataWarning>
        {
            new("burndown.scope", "O histórico considera as Tasks que estão atualmente nesta sprint e na área da equipe. Não recompõe entradas e saídas do escopo. Campos vazios em revisões anteriores contam como zero.")
        };
        var missingRemaining = snapshot.Tasks.Count(task => task.Remaining is null);
        if (missingRemaining > 0)
            warnings.Add(new("burndown.remaining.empty", $"Remaining Work está vazio em {missingRemaining} {(missingRemaining == 1 ? "Task" : "Tasks")}. Esses campos contam como 0,00 h no burndown; confira as estimativas no Azure DevOps."));
        if (!hasStarted)
            warnings.Add(new("burndown.future", "A iteração ainda não começou. A linha ideal usa o trabalho restante planejado na coleta atual; a curva real aparecerá a partir do início."));
        if (initial == 0 && changes.Length > 0)
            warnings.Add(new("burndown.zero", "O trabalho restante era zero no início. A linha ideal fica em zero; os acréscimos posteriores aparecem na curva real."));
        return Result<Burndown>.Success(new(snapshot.Sprint, snapshot.CollectedAt, initial, snapshot.Tasks.Count, points, warnings));
    }

    private static Result<Burndown> Fail(string code, string message)
        => Result<Burndown>.Failure(new(code, ErrorCategory.Incomplete, message));
}
