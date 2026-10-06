using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
using Xunit.Abstractions;

namespace TimeOps.E2E;

public sealed class ExportacaoTests(ITestOutputHelper output) : CenarioE2E(output)
{
    [Fact]
    public async Task Seleciona_datas_e_autor_externo_baixa_excel_com_layout_e_valores_corretos()
    {
        DevOps.ExportFixture = true;
        await AbrirSprintAsync();
        await Expect(Pagina.Locator(".export-panel")).ToHaveCountAsync(0);
        var toggle = Pagina.GetByRole(AriaRole.Button, new() { Name = "Exportar Excel", Exact = true });
        await toggle.FocusAsync();
        await toggle.PressAsync("Enter");
        await Expect(Pagina.GetByRole(AriaRole.Heading, new() { Name = "Exportar Excel", Exact = true })).ToBeVisibleAsync();
        await Expect(Pagina.GetByRole(AriaRole.Region, new() { Name = "Resumo da equipe" })).ToHaveCountAsync(0);
        await Expect(Pagina.GetByLabel("Capacidade até", new() { Exact = true })).ToHaveCountAsync(0);
        await Expect(Pagina.GetByLabel("Data inicial", new() { Exact = true })).ToBeFocusedAsync();
        await Expect(Pagina.GetByLabel("Data inicial", new() { Exact = true })).ToHaveValueAsync(DevOps.Inicio.ToString("yyyy-MM-dd"));
        await Pagina.GetByLabel("Data final", new() { Exact = true }).FillAsync(DevOps.Inicio.AddDays(1).ToString("yyyy-MM-dd"));
        await Pagina.GetByRole(AriaRole.Button, new() { Name = "Carregar alterações", Exact = true }).ClickAsync();
        await Expect(Pagina.Locator(".export-count")).ToHaveTextAsync("3 linhas selecionadas");
        await Pagina.GetByLabel("Pessoa", new() { Exact = true }).SelectOptionAsync("person:autor-externo");
        await Expect(Pagina.Locator(".export-count")).ToHaveTextAsync("2 linhas selecionadas");
        await Expect(Pagina.Locator(".export-table tbody tr")).ToHaveCountAsync(2);
        await Expect(Pagina.Locator(".export-preview-footer")).ToContainTextAsync("2,00 h");
        Assert.Contains(DevOps.Requisicoes, path => path == $"GET /{AzureDevOpsMock.Organizacao}/p1/t1/_apis/work/teamsettings/iterations/s1/capacities");
        Assert.DoesNotContain(DevOps.Requisicoes, path => path.Contains("projectId") || path.Contains("teamId"));

        var download = await Pagina.RunAndWaitForDownloadAsync(async () =>
            await Pagina.GetByRole(AriaRole.Button, new() { Name = "Baixar Excel", Exact = true }).ClickAsync());
        Assert.Equal($"TimeOps_Sprint E2E_{DevOps.Inicio:yyyy-MM-dd}_{DevOps.Inicio.AddDays(1):yyyy-MM-dd}.xlsx", download.SuggestedFilename);
        var path = ExportArtifacts.PathFor("tarefas.xlsx");
        await download.SaveAsAsync(path);
        Assert.Null(await download.FailureAsync());
        using var zip = ZipFile.OpenRead(path);
        using var stream = zip.GetEntry("xl/worksheets/sheet1.xml")!.Open();
        var sheet = XDocument.Load(stream);
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var rows = sheet.Descendants(ns + "row").ToArray();
        Assert.Equal(3, rows.Length);
        Assert.Equal(new[] { "x", "Atividade", "OP/PMC/PGP", "Nome da OP/PMC/PGP", "Data", "Horas", "Comentários" }, rows[0].Elements(ns + "c").Select(cell => cell.Value));
        foreach (var row in rows.Skip(1))
        {
            var cells = row.Elements(ns + "c").ToArray();
            Assert.Equal(7, cells.Length);
            Assert.Equal("João", cells[0].Value);
            Assert.Equal("Implementar login", cells[1].Value);
            Assert.Equal("Portal do cliente", cells[2].Value);
            Assert.Equal("", cells[3].Value);
            Assert.Equal(DevOps.Inicio.ToDateTime(TimeOnly.MinValue).ToOADate(), double.Parse(cells[4].Value, CultureInfo.InvariantCulture));
            Assert.Equal("", cells[6].Value);
        }
        Assert.Equal(new[] { 0.75m, 1.25m }, rows.Skip(1).Select(row => decimal.Parse(row.Elements(ns + "c").ElementAt(5).Value, CultureInfo.InvariantCulture)));
        await Pagina.ScreenshotAsync(new() { Path = ExportArtifacts.PathFor("desktop.png"), FullPage = true });

        await Pagina.GetByLabel("Data inicial", new() { Exact = true }).FillAsync(DevOps.Inicio.AddDays(1).ToString("yyyy-MM-dd"));
        await Expect(Pagina.GetByRole(AriaRole.Button, new() { Name = "Baixar Excel", Exact = true })).ToHaveCountAsync(0);
        await Pagina.GetByRole(AriaRole.Button, new() { Name = "Carregar alterações", Exact = true }).ClickAsync();
        await Expect(Pagina.Locator(".export-count")).ToHaveTextAsync("1 linha selecionada");
        await Expect(Pagina.GetByLabel("Pessoa", new() { Exact = true })).ToHaveValueAsync("");
        await Pagina.GetByLabel("Equipe", new() { Exact = true }).SelectOptionAsync("");
        await Expect(Pagina.Locator(".export-panel")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task Sem_aumentos_desabilita_download_e_falha_de_consulta_permite_tentar_novamente()
    {
        await AbrirSprintAsync();
        await Pagina.GetByRole(AriaRole.Button, new() { Name = "Exportar Excel", Exact = true }).ClickAsync();
        await Pagina.GetByRole(AriaRole.Button, new() { Name = "Carregar alterações", Exact = true }).ClickAsync();
        await Expect(Pagina.GetByRole(AriaRole.Button, new() { Name = "Baixar Excel", Exact = true })).ToBeDisabledAsync();
        await Expect(Pagina.Locator(".export-empty")).ToContainTextAsync("Não há aumentos de horas no período para a seleção atual.");
        DevOps.FalharSnapshot = true;
        await Pagina.GetByRole(AriaRole.Button, new() { Name = "Carregar alterações", Exact = true }).ClickAsync();
        await Expect(Pagina.Locator(".export-error")).ToBeVisibleAsync();
        await Expect(Pagina.GetByRole(AriaRole.Button, new() { Name = "Baixar Excel", Exact = true })).ToHaveCountAsync(0);
        DevOps.FalharSnapshot = false;
        await Pagina.GetByRole(AriaRole.Button, new() { Name = "Carregar alterações", Exact = true }).ClickAsync();
        await Expect(Pagina.Locator(".export-count")).ToHaveTextAsync("0 linhas selecionadas");
    }

    [Fact]
    public async Task Aba_de_exportacao_pode_ser_aberta_antes_de_escolher_contexto_e_nao_aparece_em_outras_visoes()
    {
        DevOps.ExportFixture = true;
        await ConectarAsync();
        await SelecionarVisaoAsync("Exportar Excel");
        await Expect(Pagina.GetByRole(AriaRole.Heading, new() { Name = "Exportar Excel", Exact = true })).ToBeVisibleAsync();
        await Expect(Pagina.Locator(".export-panel")).ToHaveCountAsync(0);
        await Pagina.GetByLabel("Projeto", new() { Exact = true }).SelectOptionAsync("p1");
        await Expect(Pagina.GetByLabel("Equipe", new() { Exact = true })).ToBeEnabledAsync();
        await Pagina.GetByLabel("Equipe", new() { Exact = true }).SelectOptionAsync("t1");
        await Expect(Pagina.GetByLabel("Sprint", new() { Exact = true })).ToBeEnabledAsync();
        await Pagina.GetByLabel("Sprint", new() { Exact = true }).SelectOptionAsync("s1");
        await Expect(Pagina.GetByRole(AriaRole.Button, new() { Name = "Carregar alterações", Exact = true })).ToBeEnabledAsync();
        await Pagina.GetByRole(AriaRole.Button, new() { Name = "Carregar alterações", Exact = true }).ClickAsync();
        await Expect(Pagina.Locator(".export-table tbody tr")).ToHaveCountAsync(4);
        await SelecionarVisaoAsync("Pessoa");
        await Expect(Pagina.Locator(".export-panel")).ToHaveCountAsync(0);
        await Expect(Pagina.Locator(".person-view")).ToBeVisibleAsync();
        await SelecionarVisaoAsync("Equipe");
        await Expect(Pagina.GetByLabel("Capacidade até", new() { Exact = true })).ToBeVisibleAsync();
    }
}

public sealed class ExportacaoMobileTests(ITestOutputHelper output) : CenarioE2E(output)
{
    protected override bool Mobile => true;

    [Fact]
    public async Task Painel_em_tela_pequena_tem_controles_visiveis_sem_rolagem_horizontal()
    {
        DevOps.ExportFixture = true;
        await AbrirSprintAsync();
        await Pagina.GetByRole(AriaRole.Button, new() { Name = "Exportar Excel", Exact = true }).TapAsync();
        await Pagina.GetByRole(AriaRole.Button, new() { Name = "Carregar alterações", Exact = true }).TapAsync();
        await Expect(Pagina.Locator(".export-count")).ToHaveTextAsync("4 linhas selecionadas");
        await Expect(Pagina.GetByRole(AriaRole.Button, new() { Name = "Baixar Excel", Exact = true })).ToBeEnabledAsync();
        Assert.True(await Pagina.Locator(".export-panel").EvaluateAsync<bool>("element => element.scrollWidth <= element.clientWidth"));
        Assert.True(await Pagina.Locator(".export-table-scroll").EvaluateAsync<bool>("element => element.scrollWidth > element.clientWidth"));
        Assert.True(await Pagina.EvaluateAsync<bool>("document.documentElement.scrollWidth <= window.innerWidth"));
        foreach (var control in await Pagina.Locator(".export-panel input, .export-panel select, .export-panel button").AllAsync())
        {
            var box = (await control.BoundingBoxAsync())!;
            Assert.InRange(box.X, 0, 390 - box.Width);
            Assert.True(box.Height >= 40);
        }
        await Pagina.ScreenshotAsync(new() { Path = ExportArtifacts.PathFor("mobile.png"), FullPage = true });
    }
}

internal static class ExportArtifacts
{
    public static string PathFor(string name)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "TimeOps.slnx"))) root = root.Parent;
        var folder = Path.Combine(root!.FullName, "artifacts", "export");
        Directory.CreateDirectory(folder);
        return Path.Combine(folder, name);
    }
}
