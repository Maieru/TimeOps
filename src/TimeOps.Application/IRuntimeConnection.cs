using TimeOps.Domain;

namespace TimeOps.Application;

public interface IRuntimeConnection
{
    string Organization { get; }
    bool IsConfigured { get; }
    Result Configure(string organization, string personalAccessToken);
    void Disconnect();
}
