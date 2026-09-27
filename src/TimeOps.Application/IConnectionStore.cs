using TimeOps.Domain;

namespace TimeOps.Application;

public sealed record SavedConnection(string Organization, string PersonalAccessToken);

public interface IConnectionStore
{
    bool IsAvailable { get; }
    Result<SavedConnection?> Read();
    Result Save(SavedConnection connection);
    Result Delete();
}
