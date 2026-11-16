using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using TimeOps.Application;
using TimeOps.Domain;
namespace TimeOps.E2E;

public sealed class GitHubHttpTests
{
    [Fact]
    public async Task Pagina_anonima_mostra_login_e_exportacao_exige_acesso()
    {
        await using var app = new TestApplication(); using var client = app.CreateClient(new() { AllowAutoRedirect = false });
        var login = await client.GetStringAsync("/login"); Assert.Contains("Entrar com GitHub", login); Assert.DoesNotContain("_framework/blazor.webassembly.js", login); Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/_framework/blazor.web.js")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/exportar?teamId=1&sprintId=1&from=2026-11-02&to=2026-11-09")).StatusCode);
        client.DefaultRequestHeaders.Add("X-Test-User", "3"); Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/exportar?teamId=1&sprintId=1&from=2026-11-02&to=2026-11-09")).StatusCode);
    }
    [Fact]
    public async Task Webhook_valida_assinatura_origem_e_deduplica_entregas()
    {
        await using var app = new TestApplication(); using var client = app.CreateClient();
        const string body = "{\"installation\":{\"id\":42},\"organization\":{\"login\":\"org\"},\"issue\":{\"node_id\":\"I-test\"}}";
        var delivery = Guid.NewGuid().ToString();
        async Task<HttpResponseMessage> Send(bool signed)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/webhooks/github"); request.Content = new StringContent(body, Encoding.UTF8, "application/json");
            request.Headers.Add("X-GitHub-Delivery", delivery); request.Headers.Add("X-GitHub-Event", "issues");
            request.Headers.Add("X-Hub-Signature-256", "sha256=" + (signed ? Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(TestApplication.WebhookSecret), Encoding.UTF8.GetBytes(body))) : new string('0', 64)));
            return await client.SendAsync(request);
        }
        Assert.Equal(HttpStatusCode.Unauthorized, (await Send(false)).StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, (await Send(true)).StatusCode); Assert.Equal(HttpStatusCode.Accepted, (await Send(true)).StatusCode);
        Assert.Equal(1, (await app.Read(d => Result<int>.Success(d.Deliveries.Count))).Value);
    }
    [Fact]
    public async Task Banco_relacional_preserva_transacao_e_resultado_incerto_sem_duplicar_implementacoes()
    {
        await using var app = new TestApplication(); using var client = app.CreateClient();
        await using var scope = app.Services.CreateAsyncScope(); var store = scope.ServiceProvider.GetRequiredService<IPlanningStore>();
        var failed = await store.ChangeAsync(d => { d.Teams.Add(new() { Id = 99, Name = "Não persistir" }); return Result<int>.Failure(new("test", ErrorCategory.Validation, "Falha esperada")); });
        Assert.True(failed.IsFailure); Assert.False((await store.ReadAsync(d => Result<bool>.Success(d.Teams.Any(t => t.Id == 99)))).Value);
        var attempts = Enumerable.Range(0, 8).Select(_ => store.ChangeAsync(d => { var id = PlanningService.Next(d.Roles.Select(r => r.Id)); d.Roles.Add(new() { Id = id, TeamId = 1, Name = "Função " + id }); return Result<int>.Success(id); }));
        var results = await Task.WhenAll(attempts); Assert.All(results, r => Assert.True(r.IsSuccess)); Assert.Equal(8, results.Select(r => r.Value).Distinct().Count());
    }
}
