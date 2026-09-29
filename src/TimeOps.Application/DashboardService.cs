using TimeOps.Domain;

namespace TimeOps.Application;

public sealed class DashboardService(IDevOpsGateway gateway, IRuntimeConnection connection, IConnectionStore connectionStore, TimeProvider clock, string timeZoneId)
{
    public bool HasConnection => connection.IsConfigured;
    public bool CanPersistConnection => connectionStore.IsAvailable;
    public string Organization => connection.Organization;

    public Result<bool> RestoreConnection()
    {
        if (!connectionStore.IsAvailable) return Result<bool>.Success(false);
        var saved = connectionStore.Read();
        if (saved.IsFailure) return Result<bool>.Failure(saved.Error!);
        if (saved.Value is null) return Result<bool>.Success(false);

        var configured = connection.Configure(saved.Value.Organization, saved.Value.PersonalAccessToken);
        return configured.IsFailure ? Result<bool>.Failure(configured.Error!) : Result<bool>.Success(true);
    }

    public Result ConfigureConnection(string organization, string personalAccessToken, bool remember)
    {
        var configured = connection.Configure(organization, personalAccessToken);
        if (configured.IsFailure) return configured;

        var stored = remember
            ? connectionStore.Save(new SavedConnection(connection.Organization, personalAccessToken.Trim()))
            : connectionStore.Delete();
        if (stored.IsFailure) connection.Disconnect();
        return stored;
    }

    public Result ForgetConnection()
    {
        connection.Disconnect();
        return connectionStore.Delete();
    }

    public DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), TimeZoneInfo.FindSystemTimeZoneById(timeZoneId)).DateTime);
    public DateTimeOffset LocalTime(DateTimeOffset value) => TimeZoneInfo.ConvertTime(value, TimeZoneInfo.FindSystemTimeZoneById(timeZoneId));

    public Task<Result<IReadOnlyList<NamedItem>>> ListProjectsAsync(CancellationToken cancellationToken = default)
        => gateway.ListProjectsAsync(cancellationToken);

    public Task<Result<IReadOnlyList<NamedItem>>> ListTeamsAsync(string projectId, CancellationToken cancellationToken = default)
        => gateway.ListTeamsAsync(projectId, cancellationToken);

    public Task<Result<IReadOnlyList<Sprint>>> ListSprintsAsync(string projectId, string teamId, CancellationToken cancellationToken = default)
        => gateway.ListSprintsAsync(projectId, teamId, cancellationToken);

    public Task<Result<EffortHistory>> GetEffortHistoryAsync(string projectId, string teamId, Sprint sprint, int days,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(projectId) || string.IsNullOrWhiteSpace(teamId) || string.IsNullOrWhiteSpace(sprint.Id))
            return Task.FromResult(Result<EffortHistory>.Failure(new("context.invalid", ErrorCategory.Validation, "Selecione projeto, equipe e sprint.")));
        if (days is not (1 or 7 or 30))
            return Task.FromResult(Result<EffortHistory>.Failure(new("history.window", ErrorCategory.Validation, "Selecione 24 horas, 7 dias ou 30 dias.")));
        var to = clock.GetUtcNow();
        return gateway.LoadEffortHistoryAsync(projectId, teamId, sprint, to.AddDays(-days), to, cancellationToken);
    }

    public async Task<Result<Burndown>> GetBurndownAsync(string projectId, string teamId, Sprint sprint,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(projectId) || string.IsNullOrWhiteSpace(teamId) || string.IsNullOrWhiteSpace(sprint.Id))
            return Result<Burndown>.Failure(new("context.invalid", ErrorCategory.Validation, "Selecione projeto, equipe e sprint."));
        if (sprint.Start is not { } start || sprint.End is null || sprint.End < start)
            return Result<Burndown>.Failure(new("sprint.dates", ErrorCategory.Incomplete, "A sprint precisa ter datas válidas para exibir o burndown."));

        TimeZoneInfo timeZone;
        try { timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId); }
        catch (TimeZoneNotFoundException)
        {
            return Result<Burndown>.Failure(new("timezone.invalid", ErrorCategory.Validation, "O fuso configurado não é válido."));
        }
        var snapshot = await gateway.LoadSnapshotAsync(projectId, teamId, sprint, false, cancellationToken);
        if (snapshot.IsFailure) return Result<Burndown>.Failure(snapshot.Error!);
        // Anchor the reconstruction to the snapshot, even when it came from cache.
        var midnight = start.ToDateTime(TimeOnly.MinValue);
        var from = new DateTimeOffset(midnight, timeZone.GetUtcOffset(midnight));
        var to = snapshot.Value.CollectedAt;
        if (to <= from)
            return BurndownCalculator.Calculate(snapshot.Value, new(from, from, to, []), timeZone);
        var history = await gateway.LoadBurndownHistoryAsync(projectId, teamId, snapshot.Value, from, to, cancellationToken);
        return history.IsFailure ? Result<Burndown>.Failure(history.Error!)
            : BurndownCalculator.Calculate(snapshot.Value, history.Value, timeZone);
    }

    public async Task<Result<Dashboard>> GetDashboardAsync(string projectId, string teamId, Sprint sprint,
        DateOnly? referenceDate, bool forceRefresh = false, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(projectId) || string.IsNullOrWhiteSpace(teamId) || string.IsNullOrWhiteSpace(sprint.Id))
            return Result<Dashboard>.Failure(new("context.invalid", ErrorCategory.Validation, "Selecione projeto, equipe e sprint."));

        DateOnly today;
        try
        {
            today = Today;
        }
        catch (TimeZoneNotFoundException)
        {
            return Result<Dashboard>.Failure(new("timezone.invalid", ErrorCategory.Validation, "O fuso configurado não é válido."));
        }

        var date = referenceDate ?? today.AddDays(-1);
        if (date > today)
            return Result<Dashboard>.Failure(new("reference.future", ErrorCategory.Validation, "A data de referência não pode ser futura."));

        var snapshot = await gateway.LoadSnapshotAsync(projectId, teamId, sprint, forceRefresh, cancellationToken);
        return snapshot.IsFailure ? Result<Dashboard>.Failure(snapshot.Error!) : MetricsCalculator.Calculate(snapshot.Value, date, today);
    }
}
