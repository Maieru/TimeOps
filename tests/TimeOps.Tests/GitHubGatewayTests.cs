using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using TimeOps.Infrastructure;
using TimeOps.Domain;

namespace TimeOps.Tests;

public sealed class GitHubGatewayTests
{
    [Fact]
    public async Task Consulta_produto_e_cria_issue_com_correlacao_sem_enviar_credenciais_ao_conteudo()
    {
        var created = 0;
        using var f = new GatewayFixture(async request =>
        {
            if (request.RequestUri!.AbsolutePath == "/graphql") return GatewayFixture.Json(new { data = new { repository = new { id = "R" }, organization = new { projectV2 = new { id = "P", url = "https://github.com/orgs/org/projects/1", fields = new { pageInfo = new { hasNextPage = false }, nodes = new[] { new { id = "SF", name = "Status", options = new[] { new { id = "todo", name = "Todo" } } } } } } } } });
            Assert.Equal("/repos/org/product/issues", request.RequestUri.AbsolutePath);
            var body = await request.Content!.ReadAsStringAsync(); Assert.Contains("timeops-implementation:", body); Assert.DoesNotContain("installation-token", body); created++;
            return GatewayFixture.Json(new { node_id = "ISSUE", number = 1, html_url = "https://github.com/org/product/issues/1", title = "Título", assignees = Array.Empty<object>() });
        });
        var info = await f.Gateway.InspectAsync("org/product", 1, default); Assert.True(info.IsSuccess); Assert.Equal("SF", info.Value.StatusFieldId);
        var issue = await f.Gateway.CreateIssueAsync("org/product", "Título", "Descrição", Guid.NewGuid(), default); Assert.True(issue.IsSuccess); Assert.Equal(1, created); Assert.Equal("ISSUE", issue.Value.NodeId);
        Assert.Equal("github.product", (await f.Gateway.InspectAsync("other/product", 1, default)).Error!.Code);
    }
    [Fact]
    public async Task Le_status_e_responsaveis_paginando_itens_do_project()
    {
        var pages = 0;
        using var f = new GatewayFixture(async request =>
        {
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync()); var query = json.RootElement.GetProperty("query").GetString()!;
            if (query.Contains("assignees(first:100)")) return GatewayFixture.Json(new { data = new { node = new { id = "ISSUE", number = 1, url = "https://github.com/org/product/issues/1", title = "Título", assignees = new { pageInfo = new { hasNextPage = false }, nodes = new[] { new { databaseId = 2 } } } } } });
            pages++; var second = pages == 2;
            return GatewayFixture.Json(new { data = new { node = new { items = new { pageInfo = new { hasNextPage = !second, endCursor = second ? null : "next" }, nodes = second ? new[] { new { id = "ITEM", isArchived = false, content = new { id = "ISSUE" }, fieldValues = new { pageInfo = new { hasNextPage = false }, nodes = new[] { new { optionId = "done", name = "Done", field = new { id = "SF" } } } } } } : [] } } } });
        });
        var result = await f.Gateway.ReadExecutionAsync(new() { ProjectNodeId = "P", StatusFieldId = "SF" }, "ISSUE", default);
        Assert.True(result.IsSuccess); Assert.Equal(2, pages); Assert.Equal("done", result.Value.StatusOptionId); Assert.Equal("2", result.Value.Issue.AssigneeIds.Single());
    }
    [Fact]
    public async Task Rejeita_campos_incompletos_e_nao_expoe_corpos_de_erro()
    {
        using var f = new GatewayFixture(_ => Task.FromResult(GatewayFixture.Json(new { data = new { repository = new { id = "R" }, organization = new { projectV2 = new { id = "P", url = "https://github.com/orgs/org/projects/1", fields = new { pageInfo = new { hasNextPage = true }, nodes = Array.Empty<object>() } } } } })));
        Assert.Equal("github.fields.incomplete", (await f.Gateway.InspectAsync("org/product", 1, default)).Error!.Code);
        using var denied = new GatewayFixture(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = new StringContent("private-server-response") }));
        var result = await denied.Gateway.FindUserAsync("member", default); Assert.Equal(ErrorCategory.Authorization, result.Error!.Category); Assert.DoesNotContain("private-server-response", result.Error.Message);
    }
    [Fact]
    public async Task Timeout_na_criacao_e_incerto_e_cancelamento_do_usuario_e_preservado()
    {
        using var f = new GatewayFixture(_ => throw new TaskCanceledException());
        var result = await f.Gateway.CreateIssueAsync("org/product", "Título", "", Guid.NewGuid(), default); Assert.Equal("github.uncertain", result.Error!.Code);
        using var canceled = new CancellationTokenSource(); canceled.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Gateway.FindUserAsync("member", canceled.Token));
    }
    private sealed class GatewayFixture : IDisposable
    {
        private readonly string key = Path.Combine(Path.GetTempPath(), "timeops-key-" + Guid.NewGuid() + ".pem");
        private readonly FakeFactory factory;
        public GitHubProjectsGateway Gateway { get; }
        public GatewayFixture(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler)
        {
            using (var rsa = RSA.Create(2048)) File.WriteAllText(key, rsa.ExportRSAPrivateKeyPem());
            factory = new FakeFactory(async request =>
            {
                if (request.RequestUri!.AbsolutePath.StartsWith("/app/installations/")) return Json(new { token = "installation-token", expires_at = "2026-11-10T18:00:00Z" });
                Assert.Equal("Bearer", request.Headers.Authorization!.Scheme); return await handler(request);
            });
            var options = Options.Create(new GitHubOptions { Organization = "org", AppId = "1", InstallationId = "42", PrivateKeyPath = key });
            Gateway = new(factory, new GitHubAppToken(factory, options, new TestClock()), options);
        }
        public static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json") };
        public void Dispose() { factory.Dispose(); File.Delete(key); }
    }
    private sealed class FakeFactory(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler) : IHttpClientFactory, IDisposable
    {
        private readonly Handler transport = new(handler);
        public HttpClient CreateClient(string name) => new(transport, false) { BaseAddress = new("https://api.github.com/") };
        public void Dispose() => transport.Dispose();
        private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler) : HttpMessageHandler
        { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => handler(request); }
    }
}
