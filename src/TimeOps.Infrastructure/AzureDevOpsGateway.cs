using System.Globalization;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using TimeOps.Application;
using TimeOps.Domain;

namespace TimeOps.Infrastructure;

public sealed partial class AzureDevOpsGateway(
    HttpClient client, RuntimeConnection connection,
    IMemoryCache cache, TimeProvider clock, ILogger<AzureDevOpsGateway> logger) : IDevOpsGateway
{
    private const string Version = "api-version=7.1";
    private static readonly string[] EffortNames = ["Microsoft.VSTS.Scheduling.CompletedWork", "Microsoft.VSTS.Scheduling.OriginalEstimate", "Microsoft.VSTS.Scheduling.RemainingWork"];
    private static readonly string[] BaseFields = ["System.Id", "System.Title", "System.WorkItemType", "System.State", "System.AreaPath", "System.IterationPath", "System.AssignedTo", "System.Parent", "System.ChangedDate"];

    public async Task<Result<IReadOnlyList<NamedItem>>> ListProjectsAsync(CancellationToken cancellationToken)
    {
        var config = ValidateConfig();
        if (config.IsFailure) return Result<IReadOnlyList<NamedItem>>.Failure(config.Error!);
        return await ListNamedAsync($"{OrgPath}/_apis/projects?$top=100&{Version}", true, cancellationToken);
    }

    public async Task<Result<IReadOnlyList<NamedItem>>> ListTeamsAsync(string projectId, CancellationToken cancellationToken)
    {
        var config = ValidateConfig();
        if (config.IsFailure) return Result<IReadOnlyList<NamedItem>>.Failure(config.Error!);
        return await ListNamedAsync($"{OrgPath}/_apis/projects/{Segment(projectId)}/teams?$top=100&{Version}", false, cancellationToken);
    }

    public async Task<Result<IReadOnlyList<Sprint>>> ListSprintsAsync(string projectId, string teamId, CancellationToken cancellationToken)
    {
        var config = ValidateConfig();
        if (config.IsFailure) return Result<IReadOnlyList<Sprint>>.Failure(config.Error!);
        var response = await SendAsync(HttpMethod.Get, $"{TeamPath(projectId, teamId)}/_apis/work/teamsettings/iterations?{Version}", null, cancellationToken);
        if (response.IsFailure) return Result<IReadOnlyList<Sprint>>.Failure(response.Error!);
        if (!TryArray(response.Value.Data, "values", out var items) && !TryArray(response.Value.Data, "value", out items))
            return Incomplete<IReadOnlyList<Sprint>>("A lista de sprints retornou dados incompletos.");
        var sprints = new List<Sprint>();
        foreach (var item in items.EnumerateArray())
        {
            var id = String(item, "id");
            var name = String(item, "name");
            var path = String(item, "path");
            if (id is null || name is null || path is null) return Incomplete<IReadOnlyList<Sprint>>("Uma sprint retornou sem identificação ou caminho.");
            var attributes = Property(item, "attributes");
            sprints.Add(new(id, name, path, Date(Property(attributes, "startDate")), Date(Property(attributes, "finishDate"))));
        }
        return Result<IReadOnlyList<Sprint>>.Success(sprints.OrderByDescending(sprint => sprint.Start).ToArray());
    }

    public async Task<Result<SprintSnapshot>> LoadSnapshotAsync(string projectId, string teamId, Sprint sprint, bool forceRefresh, CancellationToken cancellationToken)
    {
        var config = ValidateConfig();
        if (config.IsFailure) return Result<SprintSnapshot>.Failure(config.Error!);
        var key = $"snapshot:{connection.CachePartition}:{projectId}:{teamId}:{sprint.Id}";
        if (!forceRefresh && cache.TryGetValue(key, out SprintSnapshot? cached) && cached is not null)
            return Result<SprintSnapshot>.Success(cached);

        var path = TeamPath(projectId, teamId);
        var iterationPath = $"{path}/_apis/work/teamsettings/iterations/{Segment(sprint.Id)}";
        var metadata = await Task.WhenAll(new[]
        {
            $"{path}/_apis/work/teamsettings?{Version}",
            $"{iterationPath}/capacities?{Version}",
            $"{iterationPath}/teamdaysoff?{Version}",
            $"{path}/_apis/work/teamsettings/teamfieldvalues?{Version}",
            $"{OrgPath}/{Segment(projectId)}/_apis/wit/workitemtypes/Task?{Version}",
            $"{OrgPath}/{Segment(projectId)}/_apis/wit/workitemtypes/Task/states?{Version}"
        }.Select(endpoint => SendAsync(HttpMethod.Get, endpoint, null, cancellationToken)));
        foreach (var response in metadata)
            if (response.IsFailure) return Result<SprintSnapshot>.Failure(response.Error!);
        var (settings, capacity, daysOff, areas, type, states) =
            (metadata[0], metadata[1], metadata[2], metadata[3], metadata[4], metadata[5]);

        var calendarResult = ParseCalendar(settings.Value.Data, daysOff.Value.Data);
        if (calendarResult.IsFailure) return Result<SprintSnapshot>.Failure(calendarResult.Error!);
        var capacitiesResult = ParseCapacities(capacity.Value.Data);
        if (capacitiesResult.IsFailure) return Result<SprintSnapshot>.Failure(capacitiesResult.Error!);
        var areasResult = ParseAreas(areas.Value.Data);
        if (areasResult.IsFailure) return Result<SprintSnapshot>.Failure(areasResult.Error!);
        var fieldsResult = ParseFields(type.Value.Data);
        if (fieldsResult.IsFailure) return Result<SprintSnapshot>.Failure(fieldsResult.Error!);
        var statesResult = ParseStates(states.Value.Data);
        if (statesResult.IsFailure) return Result<SprintSnapshot>.Failure(statesResult.Error!);

        var tasks = await LoadTasksAsync(projectId, sprint, areasResult.Value, fieldsResult.Value, statesResult.Value, cancellationToken);
        if (tasks.IsFailure) return Result<SprintSnapshot>.Failure(tasks.Error!);
        var parents = await LoadParentsAsync(projectId, tasks.Value, cancellationToken);
        var snapshot = new SprintSnapshot(sprint, calendarResult.Value, capacitiesResult.Value, tasks.Value, fieldsResult.Value, clock.GetUtcNow(),
            parents.IsSuccess ? parents.Value : null,
            parents.IsFailure ? "Não foi possível consultar as histórias e features vinculadas. Atualize os dados ou verifique o acesso aos itens pais no Azure DevOps." : null);
        cache.Set(key, snapshot, TimeSpan.FromMinutes(5));
        return Result<SprintSnapshot>.Success(snapshot);
    }

    private async Task<Result<IReadOnlyList<NamedItem>>> ListNamedAsync(string firstPath, bool supportsContinuation, CancellationToken cancellationToken)
    {
        var items = new Dictionary<string, NamedItem>(StringComparer.OrdinalIgnoreCase);
        string? continuation = null;
        var skip = 0;
        var usedContinuation = false;
        do
        {
            var path = continuation is not null ? firstPath + "&continuationToken=" + Uri.EscapeDataString(continuation)
                : skip == 0 ? firstPath : firstPath + "&$skip=" + skip.ToString(CultureInfo.InvariantCulture);
            var response = await SendAsync(HttpMethod.Get, path, null, cancellationToken);
            if (response.IsFailure) return Result<IReadOnlyList<NamedItem>>.Failure(response.Error!);
            if (!TryArray(response.Value.Data, "value", out var values)) return Incomplete<IReadOnlyList<NamedItem>>("A listagem retornou dados incompletos.");
            var before = items.Count;
            foreach (var item in values.EnumerateArray())
            {
                var id = String(item, "id");
                var name = String(item, "name");
                if (id is null || name is null) return Incomplete<IReadOnlyList<NamedItem>>("Um item da listagem está sem ID ou nome.");
                items[id] = new(id, name);
            }
            if (values.GetArrayLength() == 100 && items.Count == before)
                return Incomplete<IReadOnlyList<NamedItem>>("A paginação da listagem não avançou.");
            continuation = supportsContinuation ? response.Value.Continuation : null;
            if (continuation is not null) usedContinuation = true;
            if (continuation is null && !usedContinuation && values.GetArrayLength() == 100) skip += 100;
            else if (continuation is null) break;
        } while (true);
        return Result<IReadOnlyList<NamedItem>>.Success(items.Values.OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase).ToArray());
    }

    private async Task<Result<IReadOnlyList<TaskWork>>> LoadTasksAsync(string projectId, Sprint sprint,
        IReadOnlyList<AreaRule> areas, EffortFields fields, IReadOnlyDictionary<string, string> categories, CancellationToken cancellationToken)
    {
        var areaClause = string.Join(" OR ", areas.Select(area => area.IncludeChildren
            ? $"[System.AreaPath] UNDER '{Wiql(area.Path)}'"
            : $"[System.AreaPath] = '{Wiql(area.Path)}'"));
        var query = $"SELECT [System.Id] FROM WorkItems WHERE [System.TeamProject] = @project AND [System.WorkItemType] = 'Task' AND [System.IterationPath] = '{Wiql(sprint.Path)}' AND ({areaClause})";
        var wiql = await SendAsync(HttpMethod.Post, $"{OrgPath}/{Segment(projectId)}/_apis/wit/wiql?$top=20000&{Version}", new WiqlRequest(query), cancellationToken);
        if (wiql.IsFailure) return Result<IReadOnlyList<TaskWork>>.Failure(wiql.Error!);
        if (!TryArray(wiql.Value.Data, "workItems", out var found)) return Incomplete<IReadOnlyList<TaskWork>>("A consulta WIQL retornou dados incompletos.");
        var ids = new List<int>();
        foreach (var item in found.EnumerateArray())
        {
            var id = Int(item, "id");
            if (id is null) return Incomplete<IReadOnlyList<TaskWork>>("A consulta WIQL retornou um item sem ID.");
            ids.Add(id.Value);
        }
        if (ids.Count >= 20000) return Incomplete<IReadOnlyList<TaskWork>>("A sprint excedeu o limite de 20.000 Tasks por consulta.");
        ids = ids.Distinct().ToList();
        if (ids.Count == 0) return Result<IReadOnlyList<TaskWork>>.Success([]);

        var selectedFields = BaseFields.Concat(fields.Completed ? [EffortNames[0]] : [])
            .Concat(fields.Original ? [EffortNames[1]] : [])
            .Concat(fields.Remaining ? [EffortNames[2]] : []).ToArray();
        var tasks = new List<TaskWork>();
        foreach (var batch in ids.Chunk(200))
        {
            var response = await SendAsync(HttpMethod.Post, $"{OrgPath}/{Segment(projectId)}/_apis/wit/workitemsbatch?{Version}",
                new WorkItemsRequest(batch, selectedFields), cancellationToken);
            if (response.IsFailure) return Result<IReadOnlyList<TaskWork>>.Failure(response.Error!);
            if (!TryArray(response.Value.Data, "value", out var values) || values.GetArrayLength() != batch.Length)
                return Incomplete<IReadOnlyList<TaskWork>>("Nem todas as Tasks da sprint puderam ser lidas.");
            foreach (var item in values.EnumerateArray())
            {
                var parsed = ParseTask(item, projectId, sprint, areas, categories);
                if (parsed.IsFailure) return Result<IReadOnlyList<TaskWork>>.Failure(parsed.Error!);
                if (parsed.Value is not null) tasks.Add(parsed.Value);
            }
        }
        if (tasks.Select(task => task.Id).Distinct().Count() != ids.Count)
            return Incomplete<IReadOnlyList<TaskWork>>("As Tasks mudaram ou ficaram indisponíveis durante a coleta. Atualize novamente.");
        return Result<IReadOnlyList<TaskWork>>.Success(tasks);
    }

    private async Task<Result<IReadOnlyList<ParentWorkItem>>> LoadParentsAsync(string projectId,
        IReadOnlyList<TaskWork> tasks, CancellationToken cancellationToken)
    {
        var parents = new Dictionary<int, ParentWorkItem>();
        var firstLevel = tasks.Where(task => task.ParentId is > 0).Select(task => task.ParentId!.Value).Distinct().ToArray();
        var first = await LoadParentBatchAsync(projectId, firstLevel, parents, cancellationToken);
        if (first.IsFailure) return Result<IReadOnlyList<ParentWorkItem>>.Failure(first.Error!);

        var secondLevel = parents.Values.Where(parent => !parent.Type.Equals("Feature", StringComparison.OrdinalIgnoreCase))
            .Where(parent => parent.ParentId is > 0).Select(parent => parent.ParentId!.Value)
            .Where(id => !parents.ContainsKey(id)).Distinct().ToArray();
        var second = await LoadParentBatchAsync(projectId, secondLevel, parents, cancellationToken);
        return second.IsFailure ? Result<IReadOnlyList<ParentWorkItem>>.Failure(second.Error!)
            : Result<IReadOnlyList<ParentWorkItem>>.Success(parents.Values.ToArray());
    }

    private async Task<Result> LoadParentBatchAsync(string projectId, int[] ids,
        Dictionary<int, ParentWorkItem> parents, CancellationToken cancellationToken)
    {
        foreach (var batch in ids.Chunk(200))
        {
            var response = await SendAsync(HttpMethod.Post, $"{OrgPath}/{Segment(projectId)}/_apis/wit/workitemsbatch?{Version}",
                new WorkItemsRequest(batch, ["System.Id", "System.Title", "System.WorkItemType", "System.Parent"]), cancellationToken);
            if (response.IsFailure) return Result.Failure(response.Error!);
            if (!TryArray(response.Value.Data, "value", out var values) || values.GetArrayLength() != batch.Length)
                return Result.Failure(new("devops.incomplete", ErrorCategory.Incomplete, "Nem todas as histórias ou features vinculadas às Tasks puderam ser lidas."));
            foreach (var item in values.EnumerateArray())
            {
                var id = Int(item, "id");
                var fields = Property(item, "fields");
                var title = String(fields, "System.Title");
                var type = String(fields, "System.WorkItemType");
                if (id is null || title is null || type is null || !batch.Contains(id.Value))
                    return Result.Failure(new("devops.incomplete", ErrorCategory.Incomplete, "Uma história ou feature retornou dados incompletos."));
                parents[id.Value] = new(id.Value, title, type, Int(fields, "System.Parent"),
                    $"https://dev.azure.com/{Segment(connection.Organization)}/{Segment(projectId)}/_workitems/edit/{id.Value}");
            }
        }
        return Result.Success();
    }

    private Result<TaskWork?> ParseTask(JsonElement item, string projectId, Sprint sprint,
        IReadOnlyList<AreaRule> areas, IReadOnlyDictionary<string, string> categories)
    {
        var id = Int(item, "id");
        var fields = Property(item, "fields");
        var title = String(fields, "System.Title");
        var type = String(fields, "System.WorkItemType");
        var state = String(fields, "System.State");
        var area = String(fields, "System.AreaPath");
        var iteration = String(fields, "System.IterationPath");
        if (id is null || title is null || type is null || state is null || area is null || iteration is null)
            return Incomplete<TaskWork?>("Uma Task retornou sem campos essenciais.");
        if (!type.Equals("Task", StringComparison.OrdinalIgnoreCase)
            || !iteration.Equals(sprint.Path, StringComparison.OrdinalIgnoreCase)
            || !areas.Any(rule => rule.Matches(area)))
            return Incomplete<TaskWork?>("As Tasks mudaram de tipo, área ou sprint durante a coleta. Atualize novamente.");
        if (!categories.TryGetValue(state, out var category)) return Incomplete<TaskWork?>("O estado de uma Task não possui categoria conhecida.");
        var assigned = Property(fields, "System.AssignedTo");
        Person? person = null;
        if (assigned.ValueKind == JsonValueKind.Object)
        {
            var identity = IdentityId(assigned);
            var name = String(assigned, "displayName");
            if (identity is null || name is null) return Incomplete<TaskWork?>("Uma Task possui responsável sem identidade estável.");
            person = new(identity, name);
        }
        else if (assigned.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(assigned.GetString()))
            return Incomplete<TaskWork?>("O responsável de uma Task não contém identidade estável.");
        return Result<TaskWork?>.Success(new(id.Value, title, person, state, category, area, iteration,
            Decimal(fields, EffortNames[0]), Decimal(fields, EffortNames[1]), Decimal(fields, EffortNames[2]),
            $"https://dev.azure.com/{Segment(connection.Organization)}/{Segment(projectId)}/_workitems/edit/{id.Value}",
            Int(fields, "System.Parent"), Int(item, "rev"),
            DateTimeOffset.TryParse(String(fields, "System.ChangedDate"), CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal, out var changedAt) ? changedAt : null));
    }

    private static Result<TeamCalendar> ParseCalendar(JsonElement settings, JsonElement daysOff)
    {
        if (!TryArray(settings, "workingDays", out var days) || !TryArray(daysOff, "daysOff", out var off))
            return Incomplete<TeamCalendar>("Os dias de trabalho ou as folgas da equipe estão indisponíveis.");
        var working = new HashSet<DayOfWeek>();
        foreach (var value in days.EnumerateArray())
        {
            if (!Enum.TryParse<DayOfWeek>(value.GetString(), true, out var day))
                return Incomplete<TeamCalendar>("Um dia de trabalho retornou com valor inválido.");
            working.Add(day);
        }
        var ranges = ParseRanges(off);
        return ranges.IsFailure ? Result<TeamCalendar>.Failure(ranges.Error!) : Result<TeamCalendar>.Success(new(working, ranges.Value));
    }

    private static Result<IReadOnlyList<MemberCapacity>> ParseCapacities(JsonElement value)
    {
        if (!TryArray(value, "teamMembers", out var members)) return Incomplete<IReadOnlyList<MemberCapacity>>("A capacidade da equipe está indisponível.");
        var result = new List<MemberCapacity>();
        foreach (var member in members.EnumerateArray())
        {
            var identity = Property(member, "teamMember");
            var id = IdentityId(identity);
            var name = String(identity, "displayName");
            if (id is null || name is null || !TryArray(member, "activities", out var activities) || !TryArray(member, "daysOff", out var off))
                return Incomplete<IReadOnlyList<MemberCapacity>>("A capacidade de uma pessoa retornou dados incompletos.");
            var hours = new List<decimal>();
            foreach (var activity in activities.EnumerateArray())
            {
                var amount = Decimal(activity, "capacityPerDay");
                if (amount is null || amount < 0) return Incomplete<IReadOnlyList<MemberCapacity>>("Uma atividade possui capacidade inválida.");
                hours.Add(amount.Value);
            }
            var ranges = ParseRanges(off);
            if (ranges.IsFailure) return Result<IReadOnlyList<MemberCapacity>>.Failure(ranges.Error!);
            result.Add(new(new(id, name), hours, ranges.Value));
        }
        return Result<IReadOnlyList<MemberCapacity>>.Success(result);
    }

    private static Result<IReadOnlyList<AreaRule>> ParseAreas(JsonElement value)
    {
        if (!TryArray(value, "values", out var areas)) return Incomplete<IReadOnlyList<AreaRule>>("As áreas da equipe estão indisponíveis.");
        var result = new List<AreaRule>();
        foreach (var area in areas.EnumerateArray())
        {
            var path = String(area, "value");
            if (path is null) return Incomplete<IReadOnlyList<AreaRule>>("Uma área da equipe retornou sem caminho.");
            result.Add(new(path, Property(area, "includeChildren").ValueKind == JsonValueKind.True));
        }
        return result.Count == 0 ? Incomplete<IReadOnlyList<AreaRule>>("A equipe não possui áreas configuradas.")
            : Result<IReadOnlyList<AreaRule>>.Success(result);
    }

    private static Result<EffortFields> ParseFields(JsonElement value)
    {
        if (!TryArray(value, "fieldInstances", out var instances) && !TryArray(value, "fields", out instances))
            return Incomplete<EffortFields>("Os campos do tipo Task estão indisponíveis.");
        var names = instances.EnumerateArray().Select(item => String(item, "referenceName"))
            .Where(name => name is not null).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return Result<EffortFields>.Success(new(names.Contains(EffortNames[0]), names.Contains(EffortNames[1]), names.Contains(EffortNames[2])));
    }

    private static Result<IReadOnlyDictionary<string, string>> ParseStates(JsonElement value)
    {
        if (!TryArray(value, "value", out var states)) return Incomplete<IReadOnlyDictionary<string, string>>("As categorias de estado das Tasks estão indisponíveis.");
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var state in states.EnumerateArray())
        {
            var name = String(state, "name");
            var category = String(state, "category");
            if (name is null || category is null) return Incomplete<IReadOnlyDictionary<string, string>>("Um estado não possui categoria.");
            result[name] = category;
        }
        return Result<IReadOnlyDictionary<string, string>>.Success(result);
    }

    private static Result<IReadOnlyList<DayRange>> ParseRanges(JsonElement values)
    {
        var ranges = new List<DayRange>();
        foreach (var value in values.EnumerateArray())
        {
            var start = Date(Property(value, "start"));
            var end = Date(Property(value, "end"));
            if (start is null || end is null || end < start) return Incomplete<IReadOnlyList<DayRange>>("Uma folga possui datas inválidas.");
            ranges.Add(new(start.Value, end.Value));
        }
        return Result<IReadOnlyList<DayRange>>.Success(ranges);
    }

    private async Task<Result<JsonResponse>> SendAsync(HttpMethod method, string path, object? body, CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(method, "https://dev.azure.com/" + path);
                var token = connection.Token;
                request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(":" + token)));
                if (body is not null) request.Content = JsonContent.Create(body, DevOpsRequestJsonContext.Default.GetTypeInfo(body.GetType())!);
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
                    var continuation = response.Headers.TryGetValues("x-ms-continuationtoken", out var values) ? values.FirstOrDefault() : null;
                    logger.LogInformation("Azure DevOps {Method} {Path}: {ElapsedMs} ms em {Attempts} tentativa(s)",
                        method.Method, request.RequestUri?.AbsolutePath, Stopwatch.GetElapsedTime(started).TotalMilliseconds, attempt + 1);
                    return Result<JsonResponse>.Success(new(document.RootElement.Clone(), continuation));
                }
                if ((response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500) && attempt < 2)
                {
                    var wait = response.Headers.RetryAfter?.Delta
                        ?? (response.Headers.RetryAfter?.Date - clock.GetUtcNow())
                        ?? TimeSpan.FromMilliseconds(250 * (1 << attempt));
                    if (wait < TimeSpan.Zero) wait = TimeSpan.Zero;
                    await Task.Delay(wait > TimeSpan.FromSeconds(10) ? TimeSpan.FromSeconds(10) : wait, cancellationToken);
                    continue;
                }
                logger.LogWarning("Azure DevOps retornou {Status} em {Path} após {ElapsedMs} ms",
                    (int)response.StatusCode, request.RequestUri?.AbsolutePath, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
                return Result<JsonResponse>.Failure(response.StatusCode switch
                {
                    HttpStatusCode.Unauthorized => new("devops.authentication", ErrorCategory.Authentication, "O PAT é inválido ou expirou."),
                    HttpStatusCode.Forbidden => new("devops.permission", ErrorCategory.Authorization, "O PAT não tem permissão para consultar estes dados."),
                    HttpStatusCode.NotFound => new("devops.not_found", ErrorCategory.NotFound, "O projeto, a equipe ou a sprint não foi encontrado."),
                    _ => new("devops.unavailable", ErrorCategory.Unavailable, "O Azure DevOps não respondeu à consulta. Tente novamente.")
                });
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && attempt < 2)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250 * (1 << attempt)), cancellationToken);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return Result<JsonResponse>.Failure(new("devops.timeout", ErrorCategory.Unavailable, "A consulta ao Azure DevOps excedeu o tempo limite."));
            }
            catch (HttpRequestException exception) when (attempt < 2)
            {
                logger.LogWarning(exception, "Falha transitória de rede ao consultar Azure DevOps");
                await Task.Delay(TimeSpan.FromMilliseconds(250 * (1 << attempt)), cancellationToken);
            }
            catch (HttpRequestException exception)
            {
                logger.LogWarning(exception, "Falha de rede ao consultar Azure DevOps");
                return Result<JsonResponse>.Failure(new("devops.network", ErrorCategory.Unavailable, "Não foi possível conectar ao Azure DevOps. Verifique a rede e se o navegador bloqueou a consulta por CORS."));
            }
            catch (JsonException exception)
            {
                logger.LogWarning(exception, "Resposta inválida do Azure DevOps");
                return Incomplete<JsonResponse>("O Azure DevOps retornou dados inválidos.");
            }
        }
        return Result<JsonResponse>.Failure(new("devops.unavailable", ErrorCategory.Unavailable, "O Azure DevOps não respondeu à consulta."));
    }

    private Result ValidateConfig()
    {
        if (!connection.IsConfigured)
            return Result.Failure(new("connection.missing", ErrorCategory.Validation, "Informe a organização e o PAT de leitura na tela inicial."));
        return Result.Success();
    }

    private string OrgPath => Segment(connection.Organization);
    private string TeamPath(string projectId, string teamId) => $"{OrgPath}/{Segment(projectId)}/{Segment(teamId)}";
    private static string Segment(string value) => Uri.EscapeDataString(value);
    private static string Wiql(string value) => value.Replace("'", "''", StringComparison.Ordinal);
    private static Result<T> Incomplete<T>(string message) => Result<T>.Failure(new("devops.incomplete", ErrorCategory.Incomplete, message));
    private static JsonElement Property(JsonElement parent, string name) => parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out var value) ? value : default;
    private static bool TryArray(JsonElement parent, string name, out JsonElement result)
    {
        result = Property(parent, name);
        return result.ValueKind == JsonValueKind.Array;
    }
    private static string? String(JsonElement parent, string name)
    {
        var value = Property(parent, name);
        return value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }
    private static int? Int(JsonElement parent, string name)
    {
        var value = Property(parent, name);
        return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) ? number : null;
    }
    private static decimal? Decimal(JsonElement parent, string name)
    {
        var value = Property(parent, name);
        return value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number) ? number : null;
    }
    private static DateOnly? Date(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.String) return null;
        var text = value.GetString();
        return text is { Length: >= 10 } && DateOnly.TryParseExact(text[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;
    }
    private static string? IdentityId(JsonElement value) => String(value, "id") ?? String(value, "descriptor") ?? String(value, "uniqueName");

    private sealed record JsonResponse(JsonElement Data, string? Continuation);
    private sealed record AreaRule(string Path, bool IncludeChildren)
    {
        public bool Matches(string actual) => actual.Equals(Path, StringComparison.OrdinalIgnoreCase)
            || IncludeChildren && actual.StartsWith(Path + "\\", StringComparison.OrdinalIgnoreCase);
    }
}
