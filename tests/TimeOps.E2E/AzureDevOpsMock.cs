using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace TimeOps.E2E;

// Each scenario owns its fixture; no request is forwarded to Azure DevOps.
public sealed class AzureDevOpsMock
{
    public const string Organizacao = "timeops-e2e";
    public const string Token = "pat-sintetico-e2e";
    public const string Iteracao = "Projeto E2E\\Sprint E2E";
    private const string Scheduling = "Microsoft.VSTS.Scheduling.";
    private readonly DateTimeOffset agora = DateTimeOffset.UtcNow;
    public DateOnly Inicio { get; }
    public DateOnly Fim => Inicio.AddDays(4);
    public DateOnly InicioFeature => Inicio.AddDays(-10);
    public DateOnly FimFeature => Fim.AddDays(10);
    public int StatusProjetos { get; set; } = 200;
    public bool FalharSnapshot { get; set; }
    public bool FalharHistorico { get; set; }
    public ConcurrentQueue<string> Requisicoes { get; } = new();
    public ConcurrentQueue<string> RequisicoesInesperadas { get; } = new();

    public AzureDevOpsMock()
    {
        var hoje = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(agora,
            TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo")).DateTime);
        // Previous Monday-Friday: always complete, with no automatic sprint selection.
        Inicio = hoje.AddDays(-((int)hoje.DayOfWeek + 6) % 7 - 7);
    }

    public async Task ResponderAsync(IRoute route)
    {
        var request = route.Request;
        var path = new Uri(request.Url).AbsolutePath;
        if (request.Method == "OPTIONS")
        {
            await ResponderJsonAsync(route, new { }, 204);
            return;
        }
        Requisicoes.Enqueue($"{request.Method} {path}");
        var authorization = await request.HeaderValueAsync("authorization");
        var expected = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes(":" + Token));
        if (!path.StartsWith($"/{Organizacao}/", StringComparison.Ordinal) || authorization != expected)
        {
            RequisicoesInesperadas.Enqueue($"Organização ou autenticação inesperada: {request.Method} {path}");
            await ResponderJsonAsync(route, new { message = "Unexpected test credentials" }, 401);
            return;
        }

        if (request.Method == "GET")
        {
            object? body = path switch
            {
                var p when p == $"/{Organizacao}/_apis/projects" => new { value = new[] { new { id = "p1", name = "Projeto E2E" } } },
                var p when p == $"/{Organizacao}/_apis/projects/p1/teams" => new { value = new[] { new { id = "t1", name = "Equipe E2E" } } },
                var p when p.EndsWith("/teamsettings/iterations", StringComparison.Ordinal) => new
                {
                    value = new[] { new { id = "s1", name = "Sprint E2E", path = Iteracao,
                        attributes = new { startDate = Data(Inicio), finishDate = Data(Fim) } } }
                },
                var p when p.EndsWith("/teamsettings", StringComparison.Ordinal) => new { workingDays = new[] { "monday", "tuesday", "wednesday", "thursday", "friday" } },
                var p when p.EndsWith("/capacities", StringComparison.Ordinal) => new
                {
                    teamMembers = new[]
                    {
                        new { teamMember = new { id = "ana", displayName = "Ana" }, activities = new[] { new { capacityPerDay = 8 } }, daysOff = Array.Empty<object>() },
                        new { teamMember = new { id = "bruno", displayName = "Bruno" }, activities = new[] { new { capacityPerDay = 6 } }, daysOff = Array.Empty<object>() }
                    }
                },
                var p when p.EndsWith("/teamdaysoff", StringComparison.Ordinal) => new { daysOff = Array.Empty<object>() },
                var p when p.EndsWith("/teamfieldvalues", StringComparison.Ordinal) => new { values = new[] { new { value = "Projeto E2E\\Equipe", includeChildren = true } } },
                var p when p.EndsWith("/workitemtypes/Task", StringComparison.Ordinal) => new
                {
                    fieldInstances = new[] { "CompletedWork", "OriginalEstimate", "RemainingWork" }.Select(name => new { referenceName = Scheduling + name })
                },
                var p when p.EndsWith("/Task/states", StringComparison.Ordinal) => new { value = new[] { new { name = "Active", category = "InProgress" } } },
                var p when p.EndsWith("/workItems/1/updates", StringComparison.Ordinal) => new { value = new[]
                {
                    Update(1, 1, DataHora(Inicio, 12), "RemainingWork", 12, 4),
                    Update(1, 2, agora.AddHours(-1).ToString("O"), "CompletedWork", 4, 8)
                } },
                var p when p.EndsWith("/workItems/2/updates", StringComparison.Ordinal) => new { value = new[]
                {
                    Update(2, 1, DataHora(Inicio.AddDays(1), 12), "RemainingWork", 10, 6)
                } },
                _ => null
            };
            if (body is not null)
            {
                var status = path.EndsWith("/_apis/projects", StringComparison.Ordinal) ? StatusProjetos
                    : FalharSnapshot && path.EndsWith("/capacities", StringComparison.Ordinal) ? 503
                    : FalharHistorico && path.EndsWith("/updates", StringComparison.Ordinal) ? 503 : 200;
                await ResponderJsonAsync(route, body, status);
                return;
            }
        }
        else if (request.Method == "POST" && path.EndsWith("/wiql", StringComparison.Ordinal))
        {
            await ResponderJsonAsync(route, new { workItems = new[] { new { id = 1 }, new { id = 2 }, new { id = 1 } } });
            return;
        }
        else if (request.Method == "POST" && path.EndsWith("/workitemsbatch", StringComparison.Ordinal))
        {
            using var payload = JsonDocument.Parse(request.PostData!);
            var ids = payload.RootElement.GetProperty("ids").EnumerateArray().Select(id => id.GetInt32()).ToArray();
            var items = ids.Select(WorkItem).ToArray();
            if (items.All(item => item is not null))
            {
                await ResponderJsonAsync(route, new { value = items });
                return;
            }
        }

        RequisicoesInesperadas.Enqueue($"{request.Method} {path}");
        await ResponderJsonAsync(route, new { message = "Unexpected Azure DevOps route" }, 400);
    }

    private object? WorkItem(int id)
    {
        var fields = new Dictionary<string, object?> { ["System.Id"] = id };
        if (id is 1 or 2)
        {
            fields["System.Title"] = id == 1 ? "Implementar login" : "Revisar relatórios";
            fields["System.WorkItemType"] = "Task";
            fields["System.State"] = "Active";
            fields["System.AreaPath"] = "Projeto E2E\\Equipe";
            fields["System.IterationPath"] = Iteracao;
            fields["System.AssignedTo"] = new { id = id == 1 ? "ana" : "bruno", displayName = id == 1 ? "Ana" : "Bruno" };
            fields["System.Parent"] = id == 1 ? 10 : 30;
            fields["System.ChangedDate"] = agora.AddMinutes(-1).ToString("O");
            fields[Scheduling + "CompletedWork"] = id == 1 ? 8 : 4;
            fields[Scheduling + "OriginalEstimate"] = id == 1 ? 12 : 10;
            fields[Scheduling + "RemainingWork"] = id == 1 ? 4 : 6;
        }
        else if (id is 10 or 20 or 30)
        {
            fields["System.Title"] = id switch { 10 => "Autenticação", 20 => "Portal do cliente", _ => "Relatórios sem prazo" };
            fields["System.WorkItemType"] = id == 10 ? "User Story" : "Feature";
            if (id == 10) fields["System.Parent"] = 20;
            if (id == 20)
            {
                fields[Scheduling + "StartDate"] = Data(InicioFeature);
                fields[Scheduling + "TargetDate"] = Data(FimFeature);
            }
        }
        else return null;
        return new { id, rev = 3, fields };
    }

    private static object Update(int task, int id, string at, string field, int before, int after) => new
    {
        id, workItemId = task, revisedBy = new { id = "ana", displayName = "Ana" },
        fields = new Dictionary<string, object>
        {
            ["System.ChangedDate"] = new { newValue = at },
            [Scheduling + field] = new { oldValue = before, newValue = after }
        }
    };

    private static string Data(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "T00:00:00Z";
    private static string DataHora(DateOnly day, int hour) => new DateTimeOffset(day.ToDateTime(new TimeOnly(hour, 0)), TimeSpan.FromHours(-3)).ToString("O");
    private static Task ResponderJsonAsync(IRoute route, object body, int status = 200) => route.FulfillAsync(new()
    {
        Status = status,
        ContentType = "application/json",
        Body = status == 204 ? "" : JsonSerializer.Serialize(body),
        Headers = new Dictionary<string, string>
        {
            ["Access-Control-Allow-Origin"] = "*",
            ["Access-Control-Allow-Methods"] = "GET, POST, OPTIONS",
            ["Access-Control-Allow-Headers"] = "authorization, content-type",
            ["Retry-After"] = "0"
        }
    });
}
