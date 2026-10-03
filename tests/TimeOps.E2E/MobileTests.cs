using Xunit.Abstractions;

namespace TimeOps.E2E;

public sealed class MobileTests(ITestOutputHelper output) : CenarioE2E(output)
{
    protected override bool Mobile => true;

    [Fact]
    public async Task Toque_no_burndown_abre_popup_e_tabela_tem_rolagem_em_tela_pequena()
    {
        await AbrirSprintAsync();
        await SelecionarVisaoAsync("Burndown da iteração");
        await Expect(Pagina.Locator(".burndown-chart")).ToBeVisibleAsync();
        await Expect(Pagina.Locator(".burndown-totals > div").First).ToContainTextAsync("22,00 h");

        await Pagina.Locator("[data-burndown-index='0']").TapAsync();

        var tooltip = Pagina.GetByRole(AriaRole.Tooltip);
        await Expect(tooltip).ToBeVisibleAsync();
        await Expect(tooltip).ToContainTextAsync("22,00 h");
        var box = (await tooltip.BoundingBoxAsync())!;
        Assert.InRange(box.X, 0, 390 - box.Width);
        await Pagina.GetByText("Ver evolução diária", new() { Exact = false }).ClickAsync();
        await Expect(Pagina.Locator(".burndown-daily tbody tr")).ToHaveCountAsync(6);
        Assert.True(await Pagina.Locator(".burndown-table-scroll")
            .EvaluateAsync<bool>("element => element.scrollWidth > element.clientWidth"));
    }
}
