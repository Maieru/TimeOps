using Xunit.Abstractions;

namespace TimeOps.E2E;

public sealed class ConexaoTests(ITestOutputHelper output) : CenarioE2E(output)
{
    [Fact]
    public async Task Conexao_salva_restaura_apos_reload_e_esquecer_remove_credencial()
    {
        await ConectarAsync();
        await Pagina.ReloadAsync();
        await Expect(Pagina.GetByLabel("Projeto", new() { Exact = true })).ToBeEnabledAsync();
        await Expect(Pagina.GetByRole(AriaRole.Heading, new() { Name = "Conectar ao Azure DevOps" })).ToHaveCountAsync(0);
        Assert.Equal(2, DevOps.Requisicoes.Count(path => path.EndsWith("/_apis/projects", StringComparison.Ordinal)));

        await Pagina.GetByRole(AriaRole.Button, new() { Name = "Esquecer e trocar conexão", Exact = true }).ClickAsync();
        await Expect(Pagina.GetByLabel("Personal Access Token", new() { Exact = true })).ToHaveValueAsync("");
        Assert.Null(await Pagina.EvaluateAsync<string?>("() => window.timeOpsConnection.read()"));

        await Pagina.ReloadAsync();
        await Expect(Pagina.GetByRole(AriaRole.Heading, new() { Name = "Conectar ao Azure DevOps" })).ToBeVisibleAsync();
        Assert.Equal(2, DevOps.Requisicoes.Count);
    }

    [Fact]
    public async Task Conexao_sem_persistencia_desaparece_ao_recarregar()
    {
        await ConectarAsync(lembrar: false);
        Assert.Null(await Pagina.EvaluateAsync<string?>("() => window.timeOpsConnection.read()"));

        await Pagina.ReloadAsync();

        await Expect(Pagina.GetByRole(AriaRole.Heading, new() { Name = "Conectar ao Azure DevOps" })).ToBeVisibleAsync();
        Assert.Single(DevOps.Requisicoes);
    }

    [Fact]
    public async Task Credencial_corrompida_pode_ser_removida_pela_interface()
    {
        await Pagina.EvaluateAsync("() => window.timeOpsConnection.save('invalid-json')");
        await Pagina.ReloadAsync();
        await Expect(Pagina.GetByRole(AriaRole.Alert)).ToContainTextAsync("Não foi possível recuperar a conexão salva.");

        await Pagina.GetByRole(AriaRole.Button, new() { Name = "Remover credencial salva", Exact = true }).ClickAsync();

        await Expect(Pagina.GetByRole(AriaRole.Alert)).ToHaveCountAsync(0);
        Assert.Null(await Pagina.EvaluateAsync<string?>("() => window.timeOpsConnection.read()"));
        await ConectarAsync();
        Assert.Single(DevOps.Requisicoes);
    }

    [Theory]
    [InlineData(401, "O PAT é inválido ou expirou.")]
    [InlineData(403, "O PAT não tem permissão para consultar estes dados.")]
    public async Task Erros_de_autenticacao_e_permissao_exibem_orientacao_sem_token(int status, string mensagem)
    {
        DevOps.StatusProjetos = status;

        await ConectarAsync(lembrar: false);

        await Expect(Pagina.GetByRole(AriaRole.Alert)).ToContainTextAsync(mensagem);
        await Expect(Pagina.Locator("body")).Not.ToContainTextAsync(AzureDevOpsMock.Token);
        Assert.Single(DevOps.Requisicoes);
    }
}
