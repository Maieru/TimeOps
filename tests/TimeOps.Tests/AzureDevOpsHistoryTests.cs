using System.Net;
using System.Collections.Concurrent;
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

    [Fact]
    public async Task Reutiliza_revisoes_sem_reler_paginas_e_filtra_novo_periodo()
    {
        var handler = new HistoryHandler { Paged = true };
        var gateway = Create(handler);
        var first = await gateway.LoadEffortHistoryAsync("p1", "t1", Sprint, From, To, CancellationToken.None);
        handler.Title = "Título atualizado";
        var second = await gateway.LoadEffortHistoryAsync("p1", "t1", Sprint, From, To.AddHours(1), CancellationToken.None);
        var earlier = await gateway.LoadEffortHistoryAsync("p1", "t1", Sprint, From, From.AddHours(1), CancellationToken.None);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.True(earlier.IsSuccess);
        Assert.Equal("Título atualizado", Assert.Single(second.Value.Changes).TaskTitle);
        Assert.Empty(earlier.Value.Changes);
        Assert.Equal(2, handler.Requests.Count(request => request.Contains("/updates")));
        Assert.Equal(3, handler.Requests.Count(request => request.Contains("/workitemsbatch")));
    }

    [Fact]
    public async Task Nova_revisao_e_nova_sessao_recarregam_as_horas()
    {
        var handler = new HistoryHandler();
        var connection = new RuntimeConnection(Options.Create(new AzureDevOpsOptions()));
        connection.Configure("org", "pat-sintetico");
        var gateway = Create(handler, connection);
        Assert.True((await gateway.LoadEffortHistoryAsync("p1", "t1", Sprint, From, To, CancellationToken.None)).IsSuccess);
        handler.Revision++;
        Assert.True((await gateway.LoadEffortHistoryAsync("p1", "t1", Sprint, From, To, CancellationToken.None)).IsSuccess);
        connection.Configure("org", "pat-sintetico");
        Assert.True((await gateway.LoadEffortHistoryAsync("p1", "t1", Sprint, From, To, CancellationToken.None)).IsSuccess);

        Assert.Equal(3, handler.Requests.Count(request => request.Contains("/updates")));
    }

    [Fact]
    public async Task Sem_numero_de_revisao_nao_reutiliza_horas()
    {
        var handler = new HistoryHandler { Revision = null };
        var gateway = Create(handler);
        Assert.True((await gateway.LoadEffortHistoryAsync("p1", "t1", Sprint, From, To, CancellationToken.None)).IsSuccess);
        Assert.True((await gateway.LoadEffortHistoryAsync("p1", "t1", Sprint, From, To, CancellationToken.None)).IsSuccess);
        Assert.Equal(2, handler.Requests.Count(request => request.Contains("/updates")));
    }

    [Fact]
    public async Task Pagina_incompleta_nao_e_armazenada_como_historico_completo()
    {
        var handler = new HistoryHandler { Paged = true, IncompleteSecondPage = true };
        var gateway = Create(handler);
        var first = await gateway.LoadEffortHistoryAsync("p1", "t1", Sprint, From, To, CancellationToken.None);
        handler.IncompleteSecondPage = false;
        var second = await gateway.LoadEffortHistoryAsync("p1", "t1", Sprint, From, To, CancellationToken.None);

        Assert.True(first.IsFailure);
        Assert.True(second.IsSuccess);
        Assert.Single(second.Value.Changes);
        Assert.Equal(4, handler.Requests.Count(request => request.Contains("/updates")));
    }

    [Fact]
    public async Task Uma_task_lenta_nao_impede_as_proximas_e_limita_concorrencia_a_oito()
    {
        var initialEight = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ninthStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSlow = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = 0;
        var active = 0;
        var concurrency = new ConcurrentBag<int>();
        var handler = new HistoryHandler
        {
            TaskCount = 10,
            BeforeUpdates = async (id, token) =>
            {
                concurrency.Add(Interlocked.Increment(ref active));
                if (Interlocked.Increment(ref started) == 8) initialEight.TrySetResult();
                try
                {
                    await initialEight.Task.WaitAsync(token);
                    if (id == 5) await releaseSlow.Task.WaitAsync(token);
                    if (id == 13) ninthStarted.TrySetResult();
                }
                finally { Interlocked.Decrement(ref active); }
            }
        };
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var loading = Create(handler).LoadEffortHistoryAsync("p1", "t1", Sprint, From, To, timeout.Token);
        try { await ninthStarted.Task.WaitAsync(timeout.Token); }
        finally { releaseSlow.TrySetResult(); }
        var result = await loading;

        Assert.True(result.IsSuccess);
        Assert.Equal(30, result.Value.Changes.Count);
        Assert.Equal(8, concurrency.Max());
    }

    [Fact]
    public async Task Falha_interrompe_novas_leituras_sem_exibir_horas_parciais()
    {
        var handler = new HistoryHandler { TaskCount = 10, InvalidHours = true };
        var result = await Create(handler).LoadEffortHistoryAsync("p1", "t1", Sprint, From, To, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCategory.Incomplete, result.Error!.Category);
        Assert.InRange(handler.Requests.Count(request => request.Contains("/updates")), 1, 8);
    }

    [Fact]
    public async Task Burndown_reutiliza_escopo_da_coleta_e_reabrir_nao_faz_nenhuma_consulta()
    {
        var handler = new HistoryHandler();
        var gateway = Create(handler);
        var snapshot = Snapshot();
        var first = await gateway.LoadBurndownHistoryAsync("p1", "t1", snapshot, From, To, CancellationToken.None);
        var second = await gateway.LoadBurndownHistoryAsync("p1", "t1", snapshot, From, To, CancellationToken.None);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.Same(first.Value, second.Value);
        Assert.Equal(EffortField.Remaining, Assert.Single(first.Value.Changes).Field);
        Assert.EndsWith("/5", first.Value.Changes[0].TaskUrl);
        Assert.Single(handler.Requests);
        Assert.Contains("/updates", handler.Requests.Single());
    }

    [Fact]
    public async Task Burndown_ignora_tasks_sem_mudanca_no_periodo_e_preserva_as_sem_data()
    {
        var handler = new HistoryHandler();
        var gateway = Create(handler);
        var snapshot = Snapshot();
        var task = snapshot.Tasks[0];
        var unchanged = snapshot with { Tasks = [task with { ChangedAt = From.AddTicks(-1) }] };
        var first = await gateway.LoadBurndownHistoryAsync("p1", "t1", unchanged, From, To, CancellationToken.None);
        Assert.True(first.IsSuccess);
        Assert.Empty(first.Value.Changes);
        Assert.Empty(handler.Requests);

        var unknownDate = snapshot with { Tasks = [task with { ChangedAt = null }] };
        var second = await gateway.LoadBurndownHistoryAsync("p1", "t1", unknownDate, From, To, CancellationToken.None);
        Assert.True(second.IsSuccess);
        Assert.Single(second.Value.Changes);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Burndown_atualizado_na_mesma_hora_e_nova_conexao_nao_reutilizam_resultado_antigo()
    {
        var handler = new HistoryHandler();
        var connection = new RuntimeConnection(Options.Create(new AzureDevOpsOptions()));
        connection.Configure("org", "pat-sintetico");
        var gateway = Create(handler, connection);
        var snapshot = Snapshot();
        Assert.True((await gateway.LoadBurndownHistoryAsync("p1", "t1", snapshot, From, To, CancellationToken.None)).IsSuccess);
        var refreshed = snapshot with { Tasks = [snapshot.Tasks[0] with { Revision = 2 }] };
        Assert.True((await gateway.LoadBurndownHistoryAsync("p1", "t1", refreshed, From, To, CancellationToken.None)).IsSuccess);
        connection.Configure("org", "pat-sintetico");
        Assert.True((await gateway.LoadBurndownHistoryAsync("p1", "t1", refreshed, From, To, CancellationToken.None)).IsSuccess);
        Assert.Equal(3, handler.Requests.Count);
    }

    [Fact]
    public async Task Burndown_nao_guarda_paginas_incompletas_e_cancela_mesmo_quando_ha_cache()
    {
        var handler = new HistoryHandler { Paged = true, IncompleteSecondPage = true };
        var gateway = Create(handler);
        var snapshot = Snapshot();
        var first = await gateway.LoadBurndownHistoryAsync("p1", "t1", snapshot, From, To, CancellationToken.None);
        Assert.True(first.IsFailure);
        handler.IncompleteSecondPage = false;
        Assert.True((await gateway.LoadBurndownHistoryAsync("p1", "t1", snapshot, From, To, CancellationToken.None)).IsSuccess);
        Assert.Equal(4, handler.Requests.Count);
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            gateway.LoadBurndownHistoryAsync("p1", "t1", snapshot, From, To, canceled.Token));
        Assert.Equal(4, handler.Requests.Count);
    }

    private static SprintSnapshot Snapshot() => new(Sprint, new(new HashSet<DayOfWeek> { DayOfWeek.Monday }, []), [],
        [new(5, "Implementar", null, "Active", "InProgress", "Projeto\\Time", Sprint.Path,
            8, 10, 8, "https://example.test/5", Revision: 1, ChangedAt: To)], new(true, true, true), To);

    [Fact]
    public async Task Burndown_nao_depende_de_alteracoes_de_completed_sem_remaining()
    {
        var handler = new HistoryHandler { InvalidHours = true, MissingChangedDate = true };
        var result = await Create(handler).LoadBurndownHistoryAsync("p1", "t1", Snapshot(), From, To, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Changes);
    }

    [Fact]
    public async Task Exportacao_le_apenas_completed_no_snapshot_e_reutiliza_paginas_entre_periodos()
    {
        var handler = new HistoryHandler { Paged = true };
        var gateway = Create(handler);
        var snapshot = Snapshot();
        var first = await gateway.LoadCompletedHistoryAsync("p1", "t1", snapshot, From, To, CancellationToken.None);
        var next = await gateway.LoadCompletedHistoryAsync("p1", "t1", snapshot, From, To.AddTicks(-1), CancellationToken.None);
        Assert.True(first.IsSuccess, first.Error?.Message);
        Assert.True(next.IsSuccess, next.Error?.Message);
        Assert.Equal(EffortField.Completed, Assert.Single(first.Value.Changes).Field);
        Assert.Equal(5m, first.Value.Changes[0].Delta);
        Assert.Equal(2, handler.Requests.Count(request => request.Contains("/updates")));
        Assert.DoesNotContain(handler.Requests, request => request.Contains("/workitemsbatch") || request.Contains("/wiql") || request.Contains("/teamfieldvalues"));
    }

    [Fact]
    public async Task Exportacao_ignora_remaining_invalido_mas_rejeita_paginas_incompletas()
    {
        var result = await Create(new HistoryHandler { InvalidOtherHours = true })
            .LoadCompletedHistoryAsync("p1", "t1", Snapshot(), From, To, CancellationToken.None);
        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal(EffortField.Completed, Assert.Single(result.Value.Changes).Field);
        var failure = await Create(new HistoryHandler { Paged = true, IncompleteSecondPage = true })
            .LoadCompletedHistoryAsync("p1", "t1", Snapshot(), From, To, CancellationToken.None);
        Assert.True(failure.IsFailure);
    }

    [Fact]
    public async Task Exportacao_pula_tasks_antigas_e_rejeita_limites_e_datas_fora_da_coleta()
    {
        var handler = new HistoryHandler();
        var gateway = Create(handler);
        var snapshot = Snapshot();
        var unchanged = snapshot with { Tasks = [snapshot.Tasks[0] with { ChangedAt = From.AddTicks(-1) }] };
        var result = await gateway.LoadCompletedHistoryAsync("p1", "t1", unchanged, From, To, CancellationToken.None);
        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Changes);
        var tooMany = snapshot with { Tasks = Enumerable.Range(1, 1001).Select(id => snapshot.Tasks[0] with { Id = id }).ToArray() };
        Assert.True((await gateway.LoadCompletedHistoryAsync("p1", "t1", tooMany, From, To, CancellationToken.None)).IsFailure);
        Assert.True((await gateway.LoadCompletedHistoryAsync("p1", "t1", snapshot, From, To.AddTicks(1), CancellationToken.None)).IsFailure);
        Assert.Empty(handler.Requests);
    }

    private static AzureDevOpsGateway Create(HistoryHandler handler, RuntimeConnection? connection = null)
    {
        if (connection is null)
        {
            connection = new RuntimeConnection(Options.Create(new AzureDevOpsOptions()));
            connection.Configure("org", "pat-sintetico");
        }
        return new(new HttpClient(handler), connection, new MemoryCache(new MemoryCacheOptions()),
            TimeProvider.System, NullLogger<AzureDevOpsGateway>.Instance);
    }

    private sealed class HistoryHandler : HttpMessageHandler
    {
        public bool Paged { get; init; }
        public bool InvalidHours { get; init; }
        public bool InvalidOtherHours { get; init; }
        public bool MissingChangedDate { get; init; }
        public int? Revision { get; set; } = 1;
        public string Title { get; set; } = "Implementar";
        public bool IncompleteSecondPage { get; set; }
        public int TaskCount { get; init; } = 1;
        public Func<int, CancellationToken, Task>? BeforeUpdates { get; init; }
        public ConcurrentQueue<string> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Enqueue(request.RequestUri!.ToString());
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
                json = JsonSerializer.Serialize(new { workItems = Enumerable.Range(5, TaskCount).Append(5).Select(id => new { id }) });
            }
            else if (path.EndsWith("/workitemsbatch", StringComparison.Ordinal))
                json = JsonSerializer.Serialize(new { value = Enumerable.Range(5, TaskCount).Select(id => new
                {
                    id, rev = Revision, fields = new Dictionary<string, string>
                    {
                        ["System.Title"] = Title, ["System.WorkItemType"] = "Task",
                        ["System.AreaPath"] = "Projeto\\Time\\API", ["System.IterationPath"] = "Projeto\\Sprint"
                    }
                }) });
            else if (path.EndsWith("/updates", StringComparison.Ordinal))
            {
                var taskId = int.Parse(path.Split('/')[^2]);
                if (BeforeUpdates is not null) await BeforeUpdates(taskId, cancellationToken);
                if (Paged && !request.RequestUri.Query.Contains("$skip=200", StringComparison.Ordinal))
                    json = JsonSerializer.Serialize(new { value = Enumerable.Range(1, 200).Select(id => new
                    {
                        id, workItemId = 5, revisedDate = "2026-09-26T10:00:00Z", revisedBy = new { id = "author-1", displayName = "Ana" }, fields = new { }
                    }) });
                else if (Paged && IncompleteSecondPage)
                    json = """{"count":1}""";
                else if (Paged)
                    json = """{"value":[{"id":201,"workItemId":5,"revisedDate":"9999-01-01T00:00:00Z","revisedBy":{"id":"author-1","displayName":"Ana"},"fields":{"System.ChangedDate":{"newValue":"2026-09-27T10:00:00Z"},"Microsoft.VSTS.Scheduling.CompletedWork":{"oldValue":3,"newValue":8}}}]}""";
                else if (InvalidHours)
                    json = """{"value":[{"id":1,"workItemId":5,"revisedDate":"9999-01-01T00:00:00Z","revisedBy":{"id":"author-1","displayName":"Ana"},"fields":{"System.ChangedDate":{"newValue":"2026-09-27T10:00:00Z"},"Microsoft.VSTS.Scheduling.CompletedWork":{"oldValue":"inválido","newValue":8}}}]}""";
                else
                    json = """{"value":[{"id":1,"workItemId":5,"revisedDate":"2026-09-26T15:30:00Z","revisedBy":{"id":"author-1","displayName":"Ana"},"fields":{"System.ChangedDate":{"newValue":"2026-09-26T15:29:00Z"},"Microsoft.VSTS.Scheduling.CompletedWork":{"oldValue":0,"newValue":99}}},{"id":2,"workItemId":5,"revisedDate":"9999-01-01T00:00:00Z","revisedBy":{"id":"author-1","displayName":"Ana"},"fields":{"System.ChangedDate":{"newValue":"2026-09-27T10:00:00Z"},"Microsoft.VSTS.Scheduling.CompletedWork":{"oldValue":3,"newValue":8}}},{"id":3,"workItemId":5,"revisedDate":"2026-09-27T12:00:00Z","revisedBy":{"id":"author-2","displayName":"Bruno"},"fields":{"System.ChangedDate":{"newValue":"2026-09-27T11:00:00Z"},"Microsoft.VSTS.Scheduling.OriginalEstimate":{"newValue":10},"Microsoft.VSTS.Scheduling.RemainingWork":{"oldValue":10,"newValue":8}}},{"id":4,"workItemId":5,"revisedDate":"2026-09-27T13:00:00Z","revisedBy":{"id":"author-1","displayName":"Ana"},"fields":{"System.ChangedDate":{"newValue":"2026-09-27T12:00:00Z"},"Microsoft.VSTS.Scheduling.CompletedWork":{"oldValue":8,"newValue":8}}},{"id":5,"workItemId":5,"revisedDate":"2026-09-27T16:00:00Z","revisedBy":{"id":"author-1","displayName":"Ana"},"fields":{"System.ChangedDate":{"newValue":"2026-09-27T16:00:00Z"},"Microsoft.VSTS.Scheduling.CompletedWork":{"oldValue":8,"newValue":11}}}]}""";
                json = json.Replace("\"workItemId\":5", $"\"workItemId\":{taskId}", StringComparison.Ordinal);
                if (MissingChangedDate) json = json.Replace("System.ChangedDate", "Ignored.ChangedDate", StringComparison.Ordinal);
                if (InvalidOtherHours) json = json.Replace("\"oldValue\":10,\"newValue\":8", "\"oldValue\":\"inválido\",\"newValue\":8", StringComparison.Ordinal);
            }
            else throw new InvalidOperationException("Rota inesperada: " + path);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }
}
