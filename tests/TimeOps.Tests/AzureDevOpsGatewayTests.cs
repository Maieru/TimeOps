using System.Net;
using System.Text;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TimeOps.Domain;
using TimeOps.Infrastructure;

namespace TimeOps.Tests;

public sealed class AzureDevOpsGatewayTests
{
    [Fact]
    public async Task Carrega_snapshot_e_deduplica_tasks_em_lotes()
    {
        var handler = new FixtureHandler();
        var gateway = Create(handler);
        var sprint = new Sprint("s1", "Sprint", "Projeto\\Sprint", new(2026, 9, 14), new(2026, 9, 25));

        var result = await gateway.LoadSnapshotAsync("p1", "t1", sprint, false, CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Single(result.Value.Tasks);
        Assert.Equal("person-1", result.Value.Tasks[0].Assignee!.Id);
        Assert.Equal(64, result.Value.Tasks[0].Completed);
        Assert.True(result.Value.Fields.Completed);
        Assert.Equal(2, result.Value.Calendar.DaysOff.Count);
        Assert.Contains(handler.Requests, request => request.Contains("workitemsbatch"));
        Assert.Contains(handler.Requests, request => request.Contains("wiql"));

        using var wiql = System.Text.Json.JsonDocument.Parse(handler.Bodies["wiql"]);
        Assert.Contains("SELECT [System.Id]", wiql.RootElement.GetProperty("query").GetString());
        using var batch = System.Text.Json.JsonDocument.Parse(handler.Bodies["workitemsbatch"]);
        Assert.Equal(5, Assert.Single(batch.RootElement.GetProperty("ids").EnumerateArray()).GetInt32());
        Assert.Equal("Fail", batch.RootElement.GetProperty("errorPolicy").GetString());
        Assert.Contains(batch.RootElement.GetProperty("fields").EnumerateArray(),
            field => field.GetString() == "Microsoft.VSTS.Scheduling.CompletedWork");

        var metrics = MetricsCalculator.Calculate(result.Value, new(2026, 9, 25), new(2026, 9, 27));
        Assert.Equal(64, metrics.Value.People.Single().Completed);
        Assert.Equal(64, metrics.Value.People.Single().Expected);
    }

    [Fact]
    public async Task Falha_se_lote_de_work_items_esta_incompleto()
    {
        var handler = new FixtureHandler { EmptyBatch = true };
        var gateway = Create(handler);
        var sprint = new Sprint("s1", "Sprint", "Projeto\\Sprint", new(2026, 9, 14), new(2026, 9, 25));
        var result = await gateway.LoadSnapshotAsync("p1", "t1", sprint, false, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCategory.Incomplete, result.Error!.Category);
    }

    [Fact]
    public async Task Nao_faz_requisicao_sem_credencial()
    {
        var handler = new FixtureHandler();
        var gateway = Create(handler, token: "");
        var result = await gateway.ListProjectsAsync(CancellationToken.None);

        Assert.Equal("connection.missing", result.Error!.Code);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Credenciais_ficam_em_sessoes_separadas_e_troca_invalida_cache()
    {
        var one = NewConnection();
        var other = NewConnection();
        Assert.False(one.IsConfigured);
        Assert.False(other.IsConfigured);
        Assert.True(one.Configure("org", "test-pat").IsSuccess);
        Assert.True(one.IsConfigured);
        Assert.False(other.IsConfigured);

        var handler = new FixtureHandler();
        var gateway = Create(handler, one);
        var sprint = new Sprint("s1", "Sprint", "Projeto\\Sprint", new(2026, 9, 14), new(2026, 9, 25));
        Assert.True((await gateway.LoadSnapshotAsync("p1", "t1", sprint, false, CancellationToken.None)).IsSuccess);
        var requestCount = handler.Requests.Count;
        Assert.True((await gateway.LoadSnapshotAsync("p1", "t1", sprint, false, CancellationToken.None)).IsSuccess);
        Assert.Equal(requestCount, handler.Requests.Count);
        Assert.True(one.Configure("org", "test-pat").IsSuccess);
        Assert.True((await gateway.LoadSnapshotAsync("p1", "t1", sprint, false, CancellationToken.None)).IsSuccess);
        Assert.True(handler.Requests.Count > requestCount);
        one.Disconnect();
        Assert.False(one.IsConfigured);
    }

    [Fact]
    public async Task Lista_todas_as_equipes_com_skip()
    {
        var handler = new FixtureHandler { PagedTeams = true };
        var result = await Create(handler).ListTeamsAsync("p1", CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal(101, result.Value.Count);
        Assert.Contains(handler.Requests, request => request.Contains("$skip=100", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Falha_de_rede_retorna_result_sem_excecao()
    {
        var connection = NewConnection();
        connection.Configure("org", "test-pat");
        var gateway = new AzureDevOpsGateway(new HttpClient(new ThrowingHandler()), connection,
            new MemoryCache(new MemoryCacheOptions()), TimeProvider.System, NullLogger<AzureDevOpsGateway>.Instance);

        var result = await gateway.ListProjectsAsync(CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("devops.network", result.Error!.Code);
    }

    private static AzureDevOpsGateway Create(FixtureHandler handler, string token = "test-pat")
    {
        var connection = NewConnection();
        if (!string.IsNullOrEmpty(token)) connection.Configure("org", token);
        return Create(handler, connection);
    }

    private static AzureDevOpsGateway Create(FixtureHandler handler, RuntimeConnection connection)
        => new(new HttpClient(handler), connection,
            new MemoryCache(new MemoryCacheOptions()), TimeProvider.System, NullLogger<AzureDevOpsGateway>.Instance);

    private static RuntimeConnection NewConnection() => new(Options.Create(new AzureDevOpsOptions()));

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new HttpRequestException("Sem conexão");
    }

    private sealed class FixtureHandler : HttpMessageHandler
    {
        public bool EmptyBatch { get; init; }
        public bool PagedTeams { get; init; }
        public List<string> Requests { get; } = [];
        public Dictionary<string, string> Bodies { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!.ToString());
            Assert.Equal("Basic", request.Headers.Authorization!.Scheme);
            Assert.Equal(":test-pat", Encoding.UTF8.GetString(Convert.FromBase64String(request.Headers.Authorization.Parameter!)));
            var path = request.RequestUri.AbsolutePath;
            if (request.Content is not null)
                Bodies[path.Split('/')[^1]] = await request.Content.ReadAsStringAsync(cancellationToken);
            if (PagedTeams && path.EndsWith("/teams", StringComparison.Ordinal))
            {
                var skipped = request.RequestUri.Query.Contains("$skip=100", StringComparison.Ordinal);
                var names = skipped ? new[] { new { id = "100", name = "Equipe 100" } }
                    : Enumerable.Range(0, 100).Select(number => new { id = number.ToString(), name = "Equipe " + number }).ToArray();
                return new HttpResponseMessage(HttpStatusCode.OK)
                { Content = new StringContent(System.Text.Json.JsonSerializer.Serialize(new { value = names }), Encoding.UTF8, "application/json") };
            }
            string json = path switch
            {
                var p when p.EndsWith("/teamsettings", StringComparison.Ordinal) => """{"workingDays":["monday","tuesday","wednesday","thursday","friday"]}""",
                var p when p.EndsWith("/capacities", StringComparison.Ordinal) => """{"teamMembers":[{"teamMember":{"id":"person-1","displayName":"Ana"},"activities":[{"capacityPerDay":6},{"capacityPerDay":2}],"daysOff":[{"start":"2026-09-16T00:00:00Z","end":"2026-09-16T00:00:00Z"}]}]}""",
                var p when p.EndsWith("/teamdaysoff", StringComparison.Ordinal) => """{"daysOff":[{"start":"2026-09-16T00:00:00Z","end":"2026-09-16T00:00:00Z"},{"start":"2026-09-17T00:00:00Z","end":"2026-09-17T00:00:00Z"}]}""",
                var p when p.EndsWith("/teamfieldvalues", StringComparison.Ordinal) => """{"values":[{"value":"Projeto\\Time","includeChildren":true}]}""",
                var p when p.EndsWith("/Task/states", StringComparison.Ordinal) => """{"value":[{"name":"Active","category":"InProgress"},{"name":"Done","category":"Completed"}]}""",
                var p when p.EndsWith("/workitemtypes/Task", StringComparison.Ordinal) => """{"fieldInstances":[{"referenceName":"Microsoft.VSTS.Scheduling.CompletedWork"},{"referenceName":"Microsoft.VSTS.Scheduling.OriginalEstimate"},{"referenceName":"Microsoft.VSTS.Scheduling.RemainingWork"}]}""",
                var p when p.EndsWith("/wiql", StringComparison.Ordinal) => """{"workItems":[{"id":5},{"id":5}]}""",
                var p when p.EndsWith("/workitemsbatch", StringComparison.Ordinal) => EmptyBatch ? """{"value":[]}""" : """{"value":[{"id":5,"fields":{"System.Id":5,"System.Title":"Implementar","System.WorkItemType":"Task","System.State":"Active","System.AreaPath":"Projeto\\Time\\API","System.IterationPath":"Projeto\\Sprint","System.AssignedTo":{"id":"person-1","displayName":"Ana"},"Microsoft.VSTS.Scheduling.CompletedWork":64,"Microsoft.VSTS.Scheduling.OriginalEstimate":80,"Microsoft.VSTS.Scheduling.RemainingWork":16}}]}""",
                _ => throw new InvalidOperationException("Rota inesperada: " + path)
            };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }
}
