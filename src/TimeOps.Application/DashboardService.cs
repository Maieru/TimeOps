using TimeOps.Domain;

namespace TimeOps.Application;

public sealed class DashboardService(IDevOpsGateway gateway, IRuntimeConnection connection, TimeProvider clock, string timeZoneId)
{
    public bool HasConnection => connection.IsConfigured;
    public string Organization => connection.Organization;
    public Result ConfigureConnection(string organization, string personalAccessToken) => connection.Configure(organization, personalAccessToken);
    public void Disconnect() => connection.Disconnect();

    public DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), TimeZoneInfo.FindSystemTimeZoneById(timeZoneId)).DateTime);
    public DateTimeOffset LocalTime(DateTimeOffset value) => TimeZoneInfo.ConvertTime(value, TimeZoneInfo.FindSystemTimeZoneById(timeZoneId));

    public Task<Result<IReadOnlyList<NamedItem>>> ListProjectsAsync(CancellationToken cancellationToken = default)
        => gateway.ListProjectsAsync(cancellationToken);

    public Task<Result<IReadOnlyList<NamedItem>>> ListTeamsAsync(string projectId, CancellationToken cancellationToken = default)
        => gateway.ListTeamsAsync(projectId, cancellationToken);

    public Task<Result<IReadOnlyList<Sprint>>> ListSprintsAsync(string projectId, string teamId, CancellationToken cancellationToken = default)
        => gateway.ListSprintsAsync(projectId, teamId, cancellationToken);

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
