namespace TimeOps.Infrastructure;

public sealed class GitHubOptions
{
    public string Organization { get; set; } = "";
    public string AppId { get; set; } = "";
    public string InstallationId { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    public string PrivateKeyPath { get; set; } = "";
    public string WebhookSecret { get; set; } = "";
    public string BootstrapAdminId { get; set; } = "";
    public bool Configured => Organization.Length > 0 && AppId.Length > 0 && InstallationId.Length > 0 && PrivateKeyPath.Length > 0;
}
