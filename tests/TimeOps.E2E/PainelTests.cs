using System.Globalization;
using Xunit.Abstractions;

namespace TimeOps.E2E;

public sealed class PainelTests(ITestOutputHelper output) : CenarioE2E(output)
{
    [Fact]
    public async Task Seleciona_contexto_exibe_totais_e_tasks_e_referencia_altera_apenas_capacidade()
    {
        await AbrirSprintAsync();
        var resumo = Pagina.GetByRole(AriaRole.Region, new() { Name = "Resumo da equipe" });
        await Expect(resumo.Locator(".summary-primary > strong")).ToHaveTextAsync("12,00 h");
        await Expect(resumo.Locator(".summary-item > strong").Nth(0)).ToHaveTextAsync("70,00 h");
        await Expect(resumo.Locator(".summary-item > strong").Nth(2)).ToHaveTextAsync("10,00 h");
        await Expect(Pagina.GetByLabel("Contexto selecionado")).ToContainTextAsync("Projeto E2E / Equipe E2E");

        await Pagina.GetByRole(AriaRole.Button, new() { Name = "Ana 1 Task", Exact = true }).ClickAsync();
        await Expect(Pagina.GetByRole(AriaRole.Link, new() { Name = "#1 · Implementar login", Exact = false })).ToBeVisibleAsync();
        await Expect(Pagina.GetByRole(AriaRole.Link, new() { Name = "#2 · Revisar relatórios", Exact = false })).ToHaveCountAsync(0);
        var consultas = DevOps.Requisicoes.Count;

        await Pagina.GetByLabel("Capacidade até", new() { Exact = true }).FillAsync(DevOps.Inicio.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        await Expect(resumo.Locator(".summary-item > strong").Nth(0)).ToHaveTextAsync("14,00 h");
        await Expect(resumo.Locator(".summary-primary > strong")).ToHaveTextAsync("12,00 h");
        Assert.Equal(consultas, DevOps.Requisicoes.Count);

        await Pagina.GetByLabel("Projeto", new() { Exact = true }).SelectOptionAsync("");
        await Expect(resumo).ToHaveCountAsync(0);
        await Expect(Pagina.GetByLabel("Equipe", new() { Exact = true })).ToBeDisabledAsync();
        await Expect(Pagina.GetByLabel("Sprint", new() { Exact = true })).ToBeDisabledAsync();
    }

    [Fact]
    public async Task Hierarquia_expande_feature_historia_e_task_com_totais_consistentes()
    {
        await AbrirSprintAsync();
        await SelecionarVisaoAsync("Features e histórias");
        var feature = Pagina.Locator(".hierarchy-feature").Filter(new() { HasText = "Portal do cliente" });
        await Expect(feature.Locator(":scope > summary .hierarchy-hours")).ToHaveTextAsync("8,00 h");
        await feature.Locator(":scope > summary").ClickAsync();
        var historia = feature.Locator(".hierarchy-story");
        await Expect(historia.Locator(":scope > summary")).ToContainTextAsync("Autenticação");
        await historia.Locator(":scope > summary").ClickAsync();
        var task = historia.Locator(".hierarchy-task");
        await Expect(task.Locator(":scope > summary")).ToContainTextAsync("Implementar login");
        await task.Locator(":scope > summary").ClickAsync();
        await Expect(task.Locator(".hierarchy-task-info")).ToContainTextAsync("Active · Ana");
        await Expect(feature.GetByRole(AriaRole.Link, new() { Name = "Abrir feature #20 no Azure DevOps", Exact = true }))
            .ToHaveAttributeAsync("href", $"https://dev.azure.com/{AzureDevOpsMock.Organizacao}/p1/_workitems/edit/20");
    }

    [Fact]
    public async Task Timeline_filtra_por_pessoa_mantem_periodo_completo_e_preserva_filtro_ao_atualizar()
    {
        await AbrirSprintAsync();
        await SelecionarVisaoAsync("Timeline de features");
        await Expect(Pagina.Locator(".timeline-table tbody tr")).ToHaveCountAsync(2);
        await Expect(Pagina.GetByText("Início e fim não informados", new() { Exact = true })).ToBeVisibleAsync();
        await Expect(Pagina.Locator(".timeline-bar")).ToHaveCountAsync(1);

        await Pagina.GetByLabel("Integrante", new() { Exact = true }).SelectOptionAsync("ana");

        await Expect(Pagina.Locator(".timeline-table tbody tr")).ToHaveCountAsync(1);
        await Expect(Pagina.Locator(".timeline-row-dates")).ToHaveTextAsync(
            $"{DevOps.InicioFeature:dd/MM/yyyy} → {DevOps.FimFeature:dd/MM/yyyy}");
        await Expect(Pagina.Locator(".timeline-row-meta")).ToContainTextAsync("25 dias · 1 Task no filtro");
        await Pagina.GetByRole(AriaRole.Button, new() { Name = "Atualizar", Exact = true }).ClickAsync();
        await Expect(Pagina.GetByRole(AriaRole.Button, new() { Name = "Atualizar", Exact = true })).ToBeEnabledAsync();
        await Expect(Pagina.GetByLabel("Integrante", new() { Exact = true })).ToHaveValueAsync("ana");
        await Expect(Pagina.Locator(".timeline-table tbody tr")).ToHaveCountAsync(1);
    }

    [Fact]
    public async Task Historico_mostra_alteracao_de_completed_e_filtra_periodo_sem_reler_atualizacoes()
    {
        await AbrirSprintAsync();
        await SelecionarVisaoAsync("Histórico de tarefas");
        await Expect(Pagina.Locator(".history-entry")).ToHaveCountAsync(1);
        await Expect(Pagina.Locator(".history-entry")).ToContainTextAsync("Horas registradas (Completed) · Ana");
        await Expect(Pagina.Locator(".history-values")).ToContainTextAsync("+4,00 h");
        await Expect(Pagina.GetByLabel("Período", new() { Exact = true })).ToBeEnabledAsync();
        var atualizacoes = DevOps.Requisicoes.Count(path => path.EndsWith("/updates", StringComparison.Ordinal));

        await Pagina.GetByLabel("Período", new() { Exact = true }).SelectOptionAsync("30");

        await Expect(Pagina.Locator(".history-entry")).ToHaveCountAsync(3);
        await Expect(Pagina.GetByLabel("Período", new() { Exact = true })).ToBeEnabledAsync();
        Assert.Equal(atualizacoes, DevOps.Requisicoes.Count(path => path.EndsWith("/updates", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Falha_ao_atualizar_preserva_ultima_coleta_e_recupera_na_proxima_tentativa()
    {
        await AbrirSprintAsync();
        DevOps.FalharSnapshot = true;
        await Pagina.GetByRole(AriaRole.Button, new() { Name = "Atualizar", Exact = true }).ClickAsync();
        await Expect(Pagina.GetByRole(AriaRole.Alert)).ToContainTextAsync("O Azure DevOps não respondeu à consulta.");
        await Expect(Pagina.GetByText("Os números abaixo são da última coleta completa", new() { Exact = false })).ToBeVisibleAsync();
        await Expect(Pagina.Locator(".summary-primary > strong")).ToHaveTextAsync("12,00 h");

        DevOps.FalharSnapshot = false;
        await Pagina.GetByRole(AriaRole.Button, new() { Name = "Atualizar", Exact = true }).ClickAsync();

        await Expect(Pagina.GetByRole(AriaRole.Button, new() { Name = "Atualizar", Exact = true })).ToBeEnabledAsync();
        await Expect(Pagina.GetByRole(AriaRole.Alert)).ToHaveCountAsync(0);
        await Expect(Pagina.GetByText("Os números abaixo são da última coleta completa", new() { Exact = false })).ToHaveCountAsync(0);
        await Expect(Pagina.Locator(".summary-primary > strong")).ToHaveTextAsync("12,00 h");
    }
}
