using Microsoft.Extensions.Options;
using TimeOps.Application;
using TimeOps.Domain;

namespace TimeOps.Infrastructure;

// Scoped: in Interactive Server this instance belongs to one Blazor circuit.
public sealed class RuntimeConnection(IOptions<AzureDevOpsOptions> options) : IRuntimeConnection
{
    private string _organization = options.Value.Organization.Trim();
    private string _token = "";

    public string Organization => _organization;
    public bool IsConfigured => !string.IsNullOrWhiteSpace(_organization) && !string.IsNullOrWhiteSpace(_token);
    internal string Token => _token;
    internal string CachePartition { get; private set; } = Guid.NewGuid().ToString("N");

    public Result Configure(string organization, string personalAccessToken)
    {
        if (string.IsNullOrWhiteSpace(organization))
            return Result.Failure(new("connection.organization", ErrorCategory.Validation, "Informe a organização do Azure DevOps."));
        if (string.IsNullOrWhiteSpace(personalAccessToken))
            return Result.Failure(new("connection.pat", ErrorCategory.Validation, "Informe o PAT de leitura."));

        _organization = organization.Trim();
        _token = personalAccessToken.Trim();
        CachePartition = Guid.NewGuid().ToString("N");
        return Result.Success();
    }

    public void Disconnect()
    {
        _token = "";
        CachePartition = Guid.NewGuid().ToString("N");
    }
}
