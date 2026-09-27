using TimeOps.Domain;

namespace TimeOps.Application;

public interface IDevOpsGateway
{
    Task<Result<IReadOnlyList<NamedItem>>> ListProjectsAsync(CancellationToken cancellationToken);
    Task<Result<IReadOnlyList<NamedItem>>> ListTeamsAsync(string projectId, CancellationToken cancellationToken);
    Task<Result<IReadOnlyList<Sprint>>> ListSprintsAsync(string projectId, string teamId, CancellationToken cancellationToken);
    Task<Result<SprintSnapshot>> LoadSnapshotAsync(string projectId, string teamId, Sprint sprint, bool forceRefresh, CancellationToken cancellationToken);
}
