using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using TimeOps.Application;
using TimeOps.Domain;

namespace TimeOps.Infrastructure;

public sealed class GitHubProjectsGateway(IHttpClientFactory clients, GitHubAppToken tokens, IOptions<GitHubOptions> options) : IGitHubProjectsGateway
{
    public async Task<Result<AppUser>> FindUserAsync(string login, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(login) || login.Length > 39 || login.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-'))
            return Fail<AppUser>("github.user", "Informe um login válido do GitHub.");
        var response = await Send(HttpMethod.Get, $"users/{Uri.EscapeDataString(login)}", null, ct);
        if (response.IsFailure) return Result<AppUser>.Failure(response.Error!);
        var user = response.Value;
        return Result<AppUser>.Success(new()
        {
            Id = user.GetProperty("id").ToString(),
            Login = user.GetProperty("login").GetString()!,
            Name = user.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String ? name.GetString()! : login
        });
    }

    public async Task<Result<GitHubProjectInfo>> InspectAsync(string repository, int projectNumber, CancellationToken ct)
    {
        if (!ValidRepository(repository) || projectNumber < 1) return Fail<GitHubProjectInfo>("github.product", "Informe organização/repositório e o número do Project da organização configurada.");
        var repo = repository.Split('/')[1];
        var response = await Graph("""
            query($owner:String!,$repo:String!,$number:Int!) {
              repository(owner:$owner,name:$repo) { id }
              organization(login:$owner) { projectV2(number:$number) {
                id url fields(first:100) { pageInfo { hasNextPage } nodes {
                  ... on ProjectV2SingleSelectField { id name options { id name } }
                } }
              } }
            }
            """, new { owner = options.Value.Organization, repo, number = projectNumber }, ct);
        if (response.IsFailure) return Result<GitHubProjectInfo>.Failure(response.Error!);
        var data = response.Value;
        if (data.GetProperty("repository").ValueKind == JsonValueKind.Null || data.GetProperty("organization").ValueKind == JsonValueKind.Null)
            return Fail<GitHubProjectInfo>("github.access", "O App precisa de acesso ao repositório e ao Project da organização.");
        var project = data.GetProperty("organization").GetProperty("projectV2");
        if (project.ValueKind == JsonValueKind.Null) return Fail<GitHubProjectInfo>("github.project", "Project não encontrado ou sem permissão de acesso.");
        var fields = project.GetProperty("fields");
        if (fields.GetProperty("pageInfo").GetProperty("hasNextPage").GetBoolean())
            return Fail<GitHubProjectInfo>("github.fields.incomplete", "O Project excedeu o limite de 100 campos. Reduza os campos para cadastrar este produto.");
        var status = fields.GetProperty("nodes").EnumerateArray().FirstOrDefault(f => f.TryGetProperty("name", out var n) && n.GetString() == "Status");
        if (status.ValueKind == JsonValueKind.Undefined) return Fail<GitHubProjectInfo>("github.status", "O Project precisa ter o campo de seleção Status.");
        var map = status.GetProperty("options").EnumerateArray().ToDictionary(v => v.GetProperty("id").GetString()!, v => v.GetProperty("name").GetString()!);
        return Result<GitHubProjectInfo>.Success(new(data.GetProperty("repository").GetProperty("id").GetString()!, project.GetProperty("id").GetString()!,
            project.GetProperty("url").GetString()!, status.GetProperty("id").GetString()!, map));
    }

    public async Task<Result<GitHubIssue?>> FindIssueAsync(string repository, Guid correlationId, CancellationToken ct)
    {
        if (!ValidRepository(repository)) return Fail<GitHubIssue?>("github.repository", "Repositório fora da organização configurada.");
        GitHubIssue? match = null;
        for (var page = 1; ; page++)
        {
            var result = await Send(HttpMethod.Get, $"repos/{RepoPath(repository)}/issues?state=all&sort=created&direction=desc&per_page=100&page={page}", null, ct);
            if (result.IsFailure) return Result<GitHubIssue?>.Failure(result.Error!);
            var items = result.Value.EnumerateArray().ToArray();
            foreach (var item in items.Where(i => !i.TryGetProperty("pull_request", out _) && i.TryGetProperty("body", out var b)
                && b.ValueKind == JsonValueKind.String && b.GetString()!.Contains(Marker(correlationId), StringComparison.Ordinal)))
            {
                if (match is not null) return Fail<GitHubIssue?>("github.duplicate", "Mais de uma issue possui o identificador desta implementação. Um administrador precisa conferir o repositório.");
                match = Issue(item);
            }
            if (items.Length < 100) return Result<GitHubIssue?>.Success(match);
        }
    }

    public async Task<Result<GitHubIssue>> CreateIssueAsync(string repository, string title, string body, Guid correlationId, CancellationToken ct)
    {
        if (!ValidRepository(repository)) return Fail<GitHubIssue>("github.repository", "Repositório fora da organização configurada.");
        var result = await Send(HttpMethod.Post, $"repos/{RepoPath(repository)}/issues", new { title, body = body + "\n\n" + Marker(correlationId) }, ct, uncertainOnFailure: true);
        return result.IsFailure ? Result<GitHubIssue>.Failure(result.Error!) : Result<GitHubIssue>.Success(Issue(result.Value));
    }

    public async Task<Result<string>> AddToProjectAsync(string projectId, string issueId, CancellationToken ct)
    {
        var result = await Graph("mutation($project:ID!,$issue:ID!){addProjectV2ItemById(input:{projectId:$project,contentId:$issue}){item{id}}}",
            new { project = projectId, issue = issueId }, ct);
        return result.IsFailure ? Result<string>.Failure(result.Error!)
            : Result<string>.Success(result.Value.GetProperty("addProjectV2ItemById").GetProperty("item").GetProperty("id").GetString()!);
    }

    public async Task<Result<GitHubExecution>> ReadExecutionAsync(PlanningProduct product, string issueId, CancellationToken ct)
    {
        var issue = await Graph("query($id:ID!){node(id:$id){... on Issue{id number url title assignees(first:100){pageInfo{hasNextPage} nodes{databaseId}}}}}", new { id = issueId }, ct);
        if (issue.IsFailure) return Result<GitHubExecution>.Failure(issue.Error!);
        var node = issue.Value.GetProperty("node");
        if (node.ValueKind == JsonValueKind.Null || !node.TryGetProperty("number", out _))
            return Fail<GitHubExecution>("github.issue.unavailable", "A issue foi removida, transferida ou está sem acesso. Confira o GitHub App.");
        if (node.GetProperty("assignees").GetProperty("pageInfo").GetProperty("hasNextPage").GetBoolean())
            return Fail<GitHubExecution>("github.assignees.incomplete", "A lista de responsáveis está incompleta.");
        // Read all project items, following pagination; never silently truncate a Project.
        for (string? cursor = null; ;)
        {
            var project = await Graph("""
                query($project:ID!,$cursor:String){node(id:$project){... on ProjectV2{items(first:100,after:$cursor){
                  pageInfo{hasNextPage endCursor} nodes{id isArchived content{... on Issue{id}}
                  fieldValues(first:100){pageInfo{hasNextPage} nodes{... on ProjectV2ItemFieldSingleSelectValue{optionId name field{... on ProjectV2SingleSelectField{id}}}}}}
                }}}}
                """, new { project = product.ProjectNodeId, cursor }, ct);
            if (project.IsFailure) return Result<GitHubExecution>.Failure(project.Error!);
            var p = project.Value.GetProperty("node");
            if (p.ValueKind == JsonValueKind.Null) return Fail<GitHubExecution>("github.project.unavailable", "O Project está sem acesso. Confira a instalação do App.");
            var items = p.GetProperty("items");
            foreach (var item in items.GetProperty("nodes").EnumerateArray())
            {
                if (!item.TryGetProperty("content", out var content) || content.ValueKind == JsonValueKind.Null
                    || !content.TryGetProperty("id", out var id) || id.GetString() != issueId) continue;
                if (item.GetProperty("fieldValues").GetProperty("pageInfo").GetProperty("hasNextPage").GetBoolean())
                    return Fail<GitHubExecution>("github.values.incomplete", "A lista de campos da implementação está incompleta.");
                var status = item.GetProperty("fieldValues").GetProperty("nodes").EnumerateArray().FirstOrDefault(v =>
                    v.TryGetProperty("field", out var field) && field.GetProperty("id").GetString() == product.StatusFieldId);
                var assignees = node.GetProperty("assignees").GetProperty("nodes").EnumerateArray()
                    .Select(a => a.GetProperty("databaseId").ToString()).ToArray();
                var info = new GitHubIssue(node.GetProperty("id").GetString()!, node.GetProperty("number").GetInt32(), node.GetProperty("url").GetString()!, node.GetProperty("title").GetString()!, assignees);
                return Result<GitHubExecution>.Success(new(info, item.GetProperty("id").GetString(),
                    status.ValueKind == JsonValueKind.Undefined ? null : status.GetProperty("optionId").GetString(),
                    status.ValueKind == JsonValueKind.Undefined ? null : status.GetProperty("name").GetString(), item.GetProperty("isArchived").GetBoolean()));
            }
            if (!items.GetProperty("pageInfo").GetProperty("hasNextPage").GetBoolean()) break;
            cursor = items.GetProperty("pageInfo").GetProperty("endCursor").GetString();
        }
        return Fail<GitHubExecution>("github.item.missing", "A issue não está mais no Project. Recoloque-a no GitHub para retomar o acompanhamento.");
    }

    private async Task<Result<JsonElement>> Graph(string query, object variables, CancellationToken ct)
    {
        var response = await Send(HttpMethod.Post, "graphql", new { query, variables }, ct);
        if (response.IsFailure) return response;
        if (response.Value.TryGetProperty("errors", out _)) return Fail<JsonElement>("github.graphql", "O GitHub não concluiu a consulta. Confira permissões e disponibilidade do Project.");
        return Result<JsonElement>.Success(response.Value.GetProperty("data"));
    }

    private async Task<Result<JsonElement>> Send(HttpMethod method, string path, object? body, CancellationToken ct, bool uncertainOnFailure = false)
    {
        var token = await tokens.GetAsync(ct);
        if (token.IsFailure) return Result<JsonElement>.Failure(token.Error!);
        try
        {
            using var request = new HttpRequestMessage(method, path);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Value);
            if (body is not null) request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
            using var response = await clients.CreateClient("GitHub").SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                if (uncertainOnFailure && (int)response.StatusCode >= 500) return Fail<JsonElement>("github.uncertain", "A criação da issue teve resultado incerto. A publicação será reconciliada antes de uma nova tentativa.");
                var category = response.StatusCode switch
                {
                    HttpStatusCode.Unauthorized => ErrorCategory.Authentication,
                    HttpStatusCode.Forbidden => ErrorCategory.Authorization,
                    HttpStatusCode.NotFound => ErrorCategory.NotFound,
                    _ => ErrorCategory.Unavailable
                };
                return Result<JsonElement>.Failure(new("github.http." + (int)response.StatusCode, category,
                    response.StatusCode == HttpStatusCode.TooManyRequests || response.Headers.RetryAfter is not null
                        ? "O GitHub limitou as chamadas. Aguarde a próxima tentativa."
                        : "O GitHub não concluiu a operação. Confira a instalação, as permissões e os dados informados."));
            }
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            return Result<JsonElement>.Success(json.RootElement.Clone());
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        { return Fail<JsonElement>(uncertainOnFailure ? "github.uncertain" : "github.network", "Não foi possível confirmar a resposta do GitHub. Acompanhe a publicação e tente sincronizar novamente."); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        { return Fail<JsonElement>(uncertainOnFailure ? "github.uncertain" : "github.timeout", "O GitHub demorou para responder. Acompanhe a publicação antes de repetir a operação."); }
    }
    private bool ValidRepository(string repository)
    {
        var parts = repository.Split('/');
        return parts.Length == 2 && parts[0].Equals(options.Value.Organization, StringComparison.OrdinalIgnoreCase)
            && parts[1].Length is > 0 and <= 100 && parts[1].All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.');
    }
    private static string RepoPath(string repository) => string.Join('/', repository.Split('/').Select(Uri.EscapeDataString));
    private static string Marker(Guid id) => $"<!-- timeops-implementation:{id:D} -->";
    private static GitHubIssue Issue(JsonElement item) => new(item.GetProperty("node_id").GetString()!, item.GetProperty("number").GetInt32(),
        item.GetProperty("html_url").GetString()!, item.GetProperty("title").GetString()!, item.GetProperty("assignees").EnumerateArray().Select(a => a.GetProperty("id").ToString()).ToArray());
    private static Result<T> Fail<T>(string code, string message) => Result<T>.Failure(new(code, ErrorCategory.Unavailable, message));
}
