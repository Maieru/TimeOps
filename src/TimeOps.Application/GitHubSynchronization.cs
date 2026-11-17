using System.Text.Json;
using TimeOps.Domain;

namespace TimeOps.Application;

public sealed class GitHubSynchronization(IPlanningStore store, IGitHubProjectsGateway github, TimeProvider clock)
{
    public async Task<Result<int>> RunAsync(CancellationToken ct = default)
    {
        var inbox = await store.ChangeAsync(d =>
        {
            foreach (var delivery in d.Deliveries.Where(x => !x.Processed))
            {
                foreach (var i in d.Implementations.Where(i => delivery.IssueNodeId is not null && i.IssueNodeId == delivery.IssueNodeId
                    || delivery.ProjectItemId is not null && i.ProjectItemId == delivery.ProjectItemId))
                {
                    var job = d.Jobs.Single(j => j.ImplementationId == i.Id);
                    job.Completed = false; job.Attempts = 0; job.NextAttemptAt = clock.GetUtcNow();
                }
                delivery.Processed = true;
            }
            return Result<bool>.Success(true);
        }, ct);
        if (inbox.IsFailure) return Result<int>.Failure(inbox.Error!);
        var count = 0;
        while (count < 20)
        {
            var claimed = await store.ChangeAsync(d =>
            {
                var job = d.Jobs.Where(j => !j.Completed && j.NextAttemptAt <= clock.GetUtcNow()).OrderBy(j => j.NextAttemptAt).FirstOrDefault();
                if (job is null) return Result<Work?>.Success(null);
                job.Attempts++; job.NextAttemptAt = clock.GetUtcNow().AddMinutes(5);
                var implementation = d.Implementations.Single(i => i.Id == job.ImplementationId);
                return Result<Work?>.Success(new(job, implementation, d.Tasks.Single(t => t.Id == implementation.TaskId), d.Products.Single(p => p.Id == implementation.ProductId)));
            }, ct);
            if (claimed.IsFailure) return Result<int>.Failure(claimed.Error!);
            if (claimed.Value is not { } work) break;
            var result = await PublishAsync(work, ct);
            var saved = await store.ChangeAsync(d =>
            {
                var job = d.Jobs.Single(j => j.Id == work.Job.Id);
                var i = d.Implementations.Single(i => i.Id == work.Implementation.Id);
                if (result.IsFailure)
                {
                    i.SyncError = result.Error!.Message;
                    if (result.Error.Code == "github.uncertain" || i.PublicationState == PublicationState.Creating)
                        i.PublicationState = PublicationState.Uncertain;
                    else if (i.IssueNodeId is null) i.PublicationState = PublicationState.Failed;
                    job.NextAttemptAt = clock.GetUtcNow().AddMinutes(Math.Min(60, Math.Pow(2, Math.Min(job.Attempts, 6))));
                    job.Completed = job.Attempts >= 8;
                }
                else
                {
                    var execution = result.Value;
                    i.IssueNodeId = execution.Issue.NodeId; i.IssueNumber = execution.Issue.Number; i.IssueUrl = execution.Issue.Url;
                    i.ProjectItemId = execution.ItemId; i.GitHubTitle = execution.Issue.Title;
                    i.AssigneeIdsJson = JsonSerializer.Serialize(execution.Issue.AssigneeIds);
                    var mapping = JsonSerializer.Deserialize<Dictionary<string, string>>(work.Product.StatusMappingJson)!;
                    i.StatusName = execution.StatusName ?? "Status não preenchido";
                    i.StateCategory = execution.StatusOptionId is not null && mapping.TryGetValue(execution.StatusOptionId, out var category) ? category : "Unknown";
                    i.Archived = execution.Archived; i.PublicationState = PublicationState.Published; i.SyncedAt = clock.GetUtcNow();
                    i.SyncError = i.StateCategory == "Unknown" ? "Configure a correspondência do status deste Project." : null;
                    job.Attempts = 0; job.Completed = false; job.NextAttemptAt = clock.GetUtcNow().AddMinutes(5);
                }
                return Result<bool>.Success(true);
            }, ct);
            if (saved.IsFailure) return Result<int>.Failure(saved.Error!);
            count++;
        }
        return Result<int>.Success(count);
    }

    private async Task<Result<GitHubExecution>> PublishAsync(Work work, CancellationToken ct)
    {
        var i = work.Implementation;
        var issueId = i.IssueNodeId;
        if (issueId is null)
        {
            GitHubIssue? issue = null;
            if (i.PublicationState is PublicationState.Creating or PublicationState.Uncertain)
            {
                var recovered = await github.FindIssueAsync(work.Product.Repository, i.CorrelationId, ct);
                if (recovered.IsFailure) return Result<GitHubExecution>.Failure(recovered.Error!);
                issue = recovered.Value;
                if (issue is null) return Result<GitHubExecution>.Failure(new("github.uncertain", ErrorCategory.Incomplete,
                    "Não foi encontrada uma issue para a criação incerta. Um gestor deve conferir o repositório e autorizar uma nova criação."));
            }
            else
            {
                var marked = await store.ChangeAsync(d =>
                {
                    d.Implementations.Single(x => x.Id == i.Id).PublicationState = PublicationState.Creating;
                    return Result<bool>.Success(true);
                }, ct);
                if (marked.IsFailure) return Result<GitHubExecution>.Failure(marked.Error!);
                var created = await github.CreateIssueAsync(work.Product.Repository, work.Task.Title,
                    work.Task.Description + $"\n\nCriada pelo TimeOps · tarefa #{work.Task.Id} · produto {work.Product.Name}.", i.CorrelationId, ct);
                if (created.IsFailure)
                {
                    // A definite API rejection did not create an issue; transport failures remain uncertain.
                    if (created.Error!.Code != "github.uncertain")
                        await store.ChangeAsync(d => { d.Implementations.Single(x => x.Id == i.Id).PublicationState = PublicationState.Failed; return Result<bool>.Success(true); }, ct);
                    return Result<GitHubExecution>.Failure(created.Error!);
                }
                issue = created.Value;
            }
            issueId = issue.NodeId;
            var saved = await store.ChangeAsync(d =>
            {
                var current = d.Implementations.Single(x => x.Id == i.Id);
                current.IssueNodeId = issue.NodeId; current.IssueNumber = issue.Number; current.IssueUrl = issue.Url;
                current.PublicationState = PublicationState.IssueCreated;
                return Result<bool>.Success(true);
            }, ct);
            if (saved.IsFailure) return Result<GitHubExecution>.Failure(saved.Error!);
        }
        if (i.PublicationState != PublicationState.Published && i.ProjectItemId is null)
        {
            var added = await github.AddToProjectAsync(work.Product.ProjectNodeId, issueId, ct);
            if (added.IsFailure) return Result<GitHubExecution>.Failure(added.Error!);
            var saved = await store.ChangeAsync(d =>
            {
                d.Implementations.Single(x => x.Id == i.Id).ProjectItemId = added.Value;
                return Result<bool>.Success(true);
            }, ct);
            if (saved.IsFailure) return Result<GitHubExecution>.Failure(saved.Error!);
        }
        return await github.ReadExecutionAsync(work.Product, issueId, ct);
    }
    private sealed record Work(PublicationJob Job, Implementation Implementation, PlanningTask Task, PlanningProduct Product);
}
