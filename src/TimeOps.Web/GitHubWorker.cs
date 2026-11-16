using TimeOps.Application;
namespace TimeOps.Web;

public sealed class GitHubWorker(IServiceScopeFactory scopes, IConfiguration configuration, ILogger<GitHubWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("GitHub:WorkerEnabled", true)) return;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var result = await scope.ServiceProvider.GetRequiredService<GitHubSynchronization>().RunAsync(stoppingToken);
                if (result.IsFailure) logger.LogWarning("Sincronização pendente: {Code}", result.Error!.Code);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError("Sincronização interrompida: {Type}", ex.GetType().Name); }
            try { if (!await timer.WaitForNextTickAsync(stoppingToken)) break; }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}
