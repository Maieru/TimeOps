using Xunit.Abstractions;

namespace TimeOps.E2E;

public sealed class BurndownTests(ITestOutputHelper output) : CenarioE2E(output)
{
    [Fact]
    public async Task Curva_tabela_e_popup_por_teclado_e_mouse_mostram_os_mesmos_valores()
    {
        await AbrirSprintAsync();
        await SelecionarVisaoAsync("Burndown da iteração");
        await Expect(Pagina.Locator(".burndown-chart")).ToBeVisibleAsync();
        await Expect(Pagina.Locator(".burndown-totals > div").Nth(0)).ToContainTextAsync("22,00 h");
        await Expect(Pagina.Locator(".burndown-totals > div").Nth(1)).ToContainTextAsync("10,00 h");
        await Pagina.GetByText("Ver evolução diária", new() { Exact = false }).ClickAsync();
        var linhas = Pagina.Locator(".burndown-daily tbody tr");
        await Expect(linhas).ToHaveCountAsync(6);
        await Expect(linhas.First.Locator("td").Nth(1)).ToHaveTextAsync("22,00 h");
        await Expect(linhas.Last.Locator("td").Nth(1)).ToHaveTextAsync("10,00 h");

        var inicio = Pagina.Locator("[data-burndown-index='0']");
        await inicio.FocusAsync();
        await Expect(inicio).ToHaveAttributeAsync("aria-expanded", "true");
        await Expect(Pagina.GetByRole(AriaRole.Tooltip)).ToContainTextAsync("22,00 h");
        await inicio.PressAsync("Escape");
        await Expect(Pagina.GetByRole(AriaRole.Tooltip)).ToHaveCountAsync(0);
        await inicio.PressAsync("Tab");
        await Expect(Pagina.Locator("[data-burndown-index='1']")).ToBeFocusedAsync();
        await Expect(Pagina.GetByRole(AriaRole.Tooltip)).ToContainTextAsync("14,00 h");

        // Move across empty plot space, far from the actual series/point.
        await Pagina.Locator("[data-burndown-index='1']").EvaluateAsync("element => element.blur()");
        var grafico = Pagina.Locator(".burndown-chart");
        await grafico.ScrollIntoViewIfNeededAsync();
        var position = await grafico.EvaluateAsync<float[]>("svg => { const p = new DOMPoint(880, 80).matrixTransform(svg.getScreenCTM()); return [p.x, p.y]; }");
        await Pagina.Mouse.MoveAsync(position[0], position[1]);
        await Expect(Pagina.Locator("[data-burndown-index='5']")).ToHaveAttributeAsync("aria-expanded", "true");
        await Expect(Pagina.GetByRole(AriaRole.Tooltip)).ToContainTextAsync("10,00 h");
        await Expect(Pagina.Locator(".burndown-guide")).ToHaveAttributeAsync("visibility", "visible");
    }

    [Fact]
    public async Task Historico_incompleto_nao_exibe_curva_parcial_e_permite_tentar_novamente()
    {
        await AbrirSprintAsync();
        DevOps.FalharHistorico = true;
        await SelecionarVisaoAsync("Burndown da iteração");
        await Expect(Pagina.GetByRole(AriaRole.Alert)).ToContainTextAsync("Não foi possível carregar o burndown.");
        await Expect(Pagina.Locator(".burndown-chart")).ToHaveCountAsync(0);

        DevOps.FalharHistorico = false;
        await Pagina.GetByRole(AriaRole.Button, new() { Name = "Tentar novamente", Exact = true }).ClickAsync();

        await Expect(Pagina.Locator(".burndown-chart")).ToBeVisibleAsync();
        await Expect(Pagina.GetByRole(AriaRole.Alert)).ToHaveCountAsync(0);
    }
}
