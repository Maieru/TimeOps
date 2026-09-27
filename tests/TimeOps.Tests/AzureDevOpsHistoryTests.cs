using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TimeOps.Domain;
using TimeOps.Infrastructure;

namespace TimeOps.Tests;

public sealed class AzureDevOpsHistoryTests
{
    private static readonly Sprint Sprint = new("s1", "Sprint", "Projeto\\Sprint", null, null);
    private static readonly DateTimeOffset From = new(2026, 9, 26, 15, 30, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset To = From.AddHours(24);

    [Fact]
    public async Task Mostra_deltas_de_horas_com_autor_e_ignora_fora_do_periodo()
    {
        var handler = new HistoryHandler();

        var result = await Create(handler).LoadEffortHistoryAsync("p1", "t1", Sprint, From, To, CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal(3, result.Value.Changes.Count);
        var completed = Assert.Single(result.Value.Changes, change => change.Field == EffortField.Completed);
        Assert.Equal(5, completed.Delta);
        Assert.Equal(3, completed.Before);
        Assert.Equal(8, completed.After);
        Assert.Equal("Ana", completed.ChangedBy!.Name);
        Assert.Equal("author-1", completed.ChangedBy.Id);
        Assert.Equal(new DateTimeOffset(2026, 9, 27, 10, 0, 0, TimeSpan.Zero), completed.ChangedAt);
        Assert.Equal(5, completed.TaskId);
        Assert.Contains("/5", completed.TaskUrl);
        Assert.Equal(-2, Assert.Single(result.Value.Changes, change => change.Field == EffortField.Remaining).Delta);
        Assert.Equal(10, Assert.Single(result.Value.Changes, change => change.Field == EffortField.Original).Delta);
        Assert.Single(handler.Requests, request => request.Contains("/updates", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Le_todas_as_paginas_de_atualizacoes_sem_duplicar_eventos()
    {
        var handler = new HistoryHandler { Paged = true };

        var result = await Create(handler).LoadEffortHistoryAsync("p1", "t1", Sprint, From, To, CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Single(result.Value.Changes);
        Assert.Equal(5, result.Value.Changes[0].Delta);
        Assert.Contains(handler.Requests, request => request.Contains("$skip=200", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Falha_sem_exibir_historico_parcial_quando_horas_sao_invalidas()
    {
        var result = await Create(new HistoryHandler { InvalidHours = true })
            .LoadEffortHistoryAsync("p1", "t1", Sprint, From, To, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCategory.Incomplete, result.Error!.Category);
    }

    private static AzureDevOpsGateway Create(HistoryHandler handler)
    {
        var connection = new RuntimeConnection(Options.Create(new AzureDevOpsOptions()));
        connection.Configure("org", "pat-sintetico");
        return new(new HttpClient(handler), connection, new MemoryCache(new MemoryCacheOptions()),
            TimeProvider.System, NullLogger<AzureDevOpsGateway>.Instance);
    }

    private sealed class HistoryHandler : HttpMessageHandler
    {
        public bool Paged { get; init; }
        public bool InvalidHours { get; init; }
        public List<string> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!.ToString());
            Assert.Equal(":pat-sintetico", Encoding.UTF8.GetString(Convert.FromBase64String(request.Headers.Authorization!.Parameter!)));
            var path = request.RequestUri.AbsolutePath;
            string json;
            if (path.EndsWith("/teamfieldvalues", StringComparison.Ordinal))
                json = """{"values":[{"value":"Projeto\\Time","includeChildren":true}]}""";
            else if (path.EndsWith("/wiql", StringComparison.Ordinal))
            {
                var body = await request.Content!.ReadAsStringAsync(cancellationToken);
                Assert.Contains("System.ChangedDate", body);
                Assert.Contains("2026-09-26T15:30:00", body);
                Assert.Contains("UNDER", body);
                Assert.Contains("timePrecision=true", request.RequestUri.Query);
                json = """{"workItems":[{"id":5},{"id":5}]}""";
            }
            else if (path.EndsWith("/workitemsbatch", StringComparison.Ordinal))
                json = """{"value":[{"id":5,"fields":{"System.Title":"Implementar","System.WorkItemType":"Task","System.AreaPath":"Projeto\\Time\\API","System.IterationPath":"Projeto\\Sprint"}}]}""";
            else if (path.EndsWith("/updates", StringComparison.Ordinal))
            {
                if (Paged && !request.RequestUri.Query.Contains("$skip=200", StringComparison.Ordinal))
                    json = JsonSerializer.Serialize(new { value = Enumerable.Range(1, 200).Select(id => new
                    {
                        id, workItemId = 5, revisedDate = "2026-09-26T10:00:00Z", revisedBy = new { id = "author-1", displayName = "Ana" }, fields = new { }
                    }) });
                else if (Paged)
                    json = """{"value":[{"id":201,"workItemId":5,"revisedDate":"9999-01-01T00:00:00Z","revisedBy":{"id":"author-1","displayName":"Ana"},"fields":{"System.ChangedDate":{"newValue":"2026-09-27T10:00:00Z"},"Microsoft.VSTS.Scheduling.CompletedWork":{"oldValue":3,"newValue":8}}}]}""";
                else if (InvalidHours)
                    json = """{"value":[{"id":1,"workItemId":5,"revisedDate":"9999-01-01T00:00:00Z","revisedBy":{"id":"author-1","displayName":"Ana"},"fields":{"System.ChangedDate":{"newValue":"2026-09-27T10:00:00Z"},"Microsoft.VSTS.Scheduling.CompletedWork":{"oldValue":"inválido","newValue":8}}}]}""";
                else
                    json = """{"value":[{"id":1,"workItemId":5,"revisedDate":"2026-09-26T15:30:00Z","revisedBy":{"id":"author-1","displayName":"Ana"},"fields":{"System.ChangedDate":{"newValue":"2026-09-26T15:29:00Z"},"Microsoft.VSTS.Scheduling.CompletedWork":{"oldValue":0,"newValue":99}}},{"id":2,"workItemId":5,"revisedDate":"9999-01-01T00:00:00Z","revisedBy":{"id":"author-1","displayName":"Ana"},"fields":{"System.ChangedDate":{"newValue":"2026-09-27T10:00:00Z"},"Microsoft.VSTS.Scheduling.CompletedWork":{"oldValue":3,"newValue":8}}},{"id":3,"workItemId":5,"revisedDate":"2026-09-27T12:00:00Z","revisedBy":{"id":"author-2","displayName":"Bruno"},"fields":{"System.ChangedDate":{"newValue":"2026-09-27T11:00:00Z"},"Microsoft.VSTS.Scheduling.OriginalEstimate":{"newValue":10},"Microsoft.VSTS.Scheduling.RemainingWork":{"oldValue":10,"newValue":8}}},{"id":4,"workItemId":5,"revisedDate":"2026-09-27T13:00:00Z","revisedBy":{"id":"author-1","displayName":"Ana"},"fields":{"System.ChangedDate":{"newValue":"2026-09-27T12:00:00Z"},"Microsoft.VSTS.Scheduling.CompletedWork":{"oldValue":8,"newValue":8}}},{"id":5,"workItemId":5,"revisedDate":"2026-09-27T16:00:00Z","revisedBy":{"id":"author-1","displayName":"Ana"},"fields":{"System.ChangedDate":{"newValue":"2026-09-27T16:00:00Z"},"Microsoft.VSTS.Scheduling.CompletedWork":{"oldValue":8,"newValue":11}}}]}""";
            }
            else throw new InvalidOperationException("Rota inesperada: " + path);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }
}
