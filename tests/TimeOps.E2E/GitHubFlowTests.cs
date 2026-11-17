using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
namespace TimeOps.E2E;

public sealed class GitHubFlowTests(Xunit.Abstractions.ITestOutputHelper output)
{
    [Fact]
    public async Task Cria_duas_implementacoes_aponta_horas_e_consulta_todas_as_visoes()
    {
        await using var app = new TestApplication(); app.UseKestrel(0); using var client = app.CreateClient();
        using var playwright = await Playwright.CreateAsync(); await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true, ExecutablePath = Environment.GetEnvironmentVariable("E2E_BROWSER_PATH"), Args = ["--no-sandbox"] });
        await using var context = await browser.NewContextAsync(new() { BaseURL = client.BaseAddress!.ToString(), Locale = "pt-BR", TimezoneId = "America/Sao_Paulo", ViewportSize = new() { Width = 1440, Height = 1000 }, ExtraHTTPHeaders = new Dictionary<string, string> { { "X-Test-User", "1" } } });
        // Native downloads use the browser's cookie jar rather than renderer-only extra headers.
        await context.AddCookiesAsync([new() { Name="TimeOps.TestUser",Value="1",Url=client.BaseAddress.ToString(),HttpOnly=true }]);
        var page = await context.NewPageAsync(); page.Console += (_, e) => { if (e.Type == "error") output.WriteLine(e.Text); }; page.RequestFailed += (_, r) => output.WriteLine(r.Url + " " + r.Failure); var errors = new List<string>(); page.PageError += (_, e) => errors.Add(e);
        page.SetDefaultTimeout(15000);
        await page.GotoAsync("/tarefas");await Expect(page.Locator(".sidebar-nav a.active")).ToHaveTextAsync("Tarefas"); await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Tarefas e implementações" })).ToBeVisibleAsync();
        await page.GetByRole(AriaRole.Tab, new() { Name = "Nova tarefa", Exact = true }).ClickAsync();
        await page.GetByLabel("Título da tarefa", new() { Exact = true }).FillAsync("Autenticação em dois produtos"); await page.GetByLabel("Descrição", new() { Exact = true }).FillAsync("Dados sintéticos para validar a integração.");
        await page.GetByRole(AriaRole.Checkbox, new() { NameRegex = new Regex("Produto A") }).CheckAsync(); await page.GetByRole(AriaRole.Checkbox, new() { NameRegex = new Regex("Produto B") }).CheckAsync();
        foreach (var id in new[] { 1, 2 }) { await page.Locator($"#task-sprint-{id}").SelectOptionAsync("1"); await page.Locator($"#task-original-{id}").FillAsync("8"); await page.Locator($"#task-remaining-{id}").FillAsync("5"); }
        await page.GetByRole(AriaRole.Button, new() { Name = "Criar tarefa e publicar no GitHub" }).ClickAsync(); await Expect(page.GetByRole(AriaRole.Status)).ToContainTextAsync("Tarefa criada");
        await page.GetByRole(AriaRole.Tab, new() { Name = "Tarefas", Exact = true }).ClickAsync();
        await Expect(page.Locator(".task-index li")).ToHaveCountAsync(1);
        await Expect(page.Locator(".task-group")).ToHaveCountAsync(0);
        await page.GetByLabel("Buscar tarefa").FillAsync("inexistente");
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Nenhuma tarefa encontrada" })).ToBeVisibleAsync();
        await page.GetByLabel("Buscar tarefa").FillAsync("Autenticação");
        await Capture(page, "tasks-list");
        await page.Locator(".task-index button").ClickAsync();
        await app.Sync(); await page.GetByRole(AriaRole.Button, new() { Name = "Atualizar dados" }).ClickAsync(); await Expect(page.Locator(".task-group a[target='_blank']")).ToHaveCountAsync(2); Assert.Equal(2, app.GitHub.Created);
        await Expect(page.Locator(".task-group")).ToContainTextAsync("09/11 22:00");
        await page.GetByRole(AriaRole.Button,new(){Name="Editar tarefa",Exact=true}).ClickAsync();await Expect(page.Locator("#edit-title")).ToBeFocusedAsync();
        await page.GetByRole(AriaRole.Button,new(){Name="Salvar tarefa",Exact=true}).ClickAsync();await Expect(page.GetByRole(AriaRole.Status)).ToContainTextAsync("Tarefa atualizada");await page.GetByRole(AriaRole.Button,new(){Name="Cancelar edição",Exact=true}).ClickAsync();
        await page.GetByRole(AriaRole.Button,new(){Name="Editar planejamento e esforço"}).First.ClickAsync();await Expect(page.Locator("#edit-implementation-sprint")).ToBeFocusedAsync();
        await page.GetByRole(AriaRole.Button,new(){Name="Salvar planejamento"}).ClickAsync();await Expect(page.GetByRole(AriaRole.Status)).ToContainTextAsync("Planejamento atualizado");
        await Capture(page, "tasks");
        await page.GotoAsync("/horas");await page.GetByRole(AriaRole.Button,new(){Name="Novo apontamento",Exact=true}).ClickAsync();await Expect(page.Locator("#hours-date")).ToHaveValueAsync("2026-11-09"); await page.GetByLabel("Implementação", new() { Exact = true }).SelectOptionAsync("1"); await page.GetByLabel("Pessoa que trabalhou").SelectOptionAsync("2"); await page.GetByLabel("Sprint do apontamento").SelectOptionAsync("1"); await page.GetByLabel("Data trabalhada").FillAsync("2026-11-09"); await page.GetByLabel("Horas realizadas").FillAsync("3"); await page.GetByRole(AriaRole.Button, new() { Name = "Registrar horas" }).ClickAsync(); await Expect(page.GetByRole(AriaRole.Status)).ToContainTextAsync("Apontamento salvo");
        await page.GetByRole(AriaRole.Button,new(){Name="Corrigir",Exact=true}).ClickAsync();await Expect(page.Locator("#hours-value")).ToBeFocusedAsync();await page.Locator("#hours-value").FillAsync("4");await page.GetByRole(AriaRole.Button,new(){Name="Salvar correção"}).ClickAsync();await Expect(page.Locator(".compact-table tbody")).ToContainTextAsync("4,00 h");
        await page.GetByRole(AriaRole.Button,new(){Name="Corrigir",Exact=true}).ClickAsync();await Expect(page.Locator("#hours-value")).ToBeFocusedAsync();await Expect(page.Locator("#hours-value")).ToHaveValueAsync(new Regex(@"^4(?:\.0+)?$"));await page.Locator("#hours-value").FillAsync("3");await page.GetByRole(AriaRole.Button,new(){Name="Salvar correção"}).ClickAsync();await Expect(page.Locator(".compact-table tbody")).ToContainTextAsync("3,00 h");
        await page.GetByRole(AriaRole.Button,new(){Name="Anular",Exact=true}).ClickAsync();await Expect(page.Locator(".compact-table tbody")).ToContainTextAsync("Anulado");
        await page.GetByRole(AriaRole.Button,new(){Name="Corrigir",Exact=true}).ClickAsync();await Expect(page.Locator("#hours-value")).ToBeFocusedAsync();await page.Locator("#hours-value").FillAsync("5");await page.GetByRole(AriaRole.Button,new(){Name="Salvar correção"}).ClickAsync();await Expect(page.Locator(".compact-table tbody")).ToContainTextAsync("5,00 h");
        Assert.Equal(0,(await app.Read(d=>TimeOps.Domain.Result<decimal>.Success(d.WorkLogs.Where(w=>!w.Voided).Sum(w=>w.Hours)))).Value);
        await page.GetByRole(AriaRole.Button,new(){Name="Novo apontamento",Exact=true}).ClickAsync();await page.GetByLabel("Implementação",new(){Exact=true}).SelectOptionAsync("1");await page.GetByLabel("Pessoa que trabalhou").SelectOptionAsync("2");await page.GetByLabel("Sprint do apontamento").SelectOptionAsync("1");await page.GetByLabel("Horas realizadas").FillAsync("3");await page.GetByRole(AriaRole.Button,new(){Name="Registrar horas"}).ClickAsync();await Expect(page.Locator(".compact-table tbody tr")).ToHaveCountAsync(2);await Expect(page.Locator(".compact-table tbody")).ToContainTextAsync("Registrado");
        await Capture(page,"hours");
        await page.GotoAsync("/"); await Expect(page.Locator(".sidebar-nav a[aria-current=page]")).ToHaveTextAsync("Painel");await Expect(page.Locator(".summary-primary strong")).ToContainTextAsync("3,00");
        await page.GetByRole(AriaRole.Tab, new() { Name = "Por pessoa", Exact = true }).ClickAsync(); await Expect(page.GetByLabel("Pessoa", new() { Exact = true })).ToBeVisibleAsync(); await page.GetByLabel("Pessoa", new() { Exact = true }).SelectOptionAsync("2"); await Expect(page.Locator(".person-view")).ToContainTextAsync("3,00");
        await page.GetByRole(AriaRole.Tab, new() { Name = "Por tarefa", Exact = true }).ClickAsync(); await Expect(page.Locator(".task-hierarchy")).ToContainTextAsync("Autenticação em dois produtos");
        await page.GetByRole(AriaRole.Tab, new() { Name = "Timeline", Exact = true }).ClickAsync(); await Expect(page.Locator(".timeline-panel")).ToContainTextAsync("Autenticação em dois produtos");
        await page.GetByRole(AriaRole.Tab, new() { Name = "Histórico", Exact = true }).ClickAsync(); await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Alterações de esforço e planejamento" })).ToBeVisibleAsync();
        await page.GetByRole(AriaRole.Tab, new() { Name = "Burndown", Exact = true }).ClickAsync(); await Expect(page.Locator(".burndown-chart")).ToBeVisibleAsync();
        await page.GetByRole(AriaRole.Tab, new() { Name = "Exportar Excel", Exact = true }).ClickAsync(); await page.GetByRole(AriaRole.Button, new() { Name = "Carregar prévia" }).ClickAsync(); await Expect(page.GetByRole(AriaRole.Link, new() { Name = "Baixar Excel" })).ToBeVisibleAsync(); var download = await page.RunAndWaitForDownloadAsync(() => page.GetByRole(AriaRole.Link, new() { Name = "Baixar Excel" }).ClickAsync()); Assert.EndsWith(".xlsx", download.SuggestedFilename);
        await using (var exported = await download.CreateReadStreamAsync()) { Assert.NotNull(exported); var signature = new byte[4]; await exported.ReadExactlyAsync(signature); Assert.Equal(new byte[]{0x50,0x4b,0x03,0x04}, signature); }
        await page.GetByRole(AriaRole.Tab, new() { Name = "Equipe", Exact = true }).ClickAsync(); await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Painel da equipe", Exact = true })).ToBeVisibleAsync();
        var root = FindRoot(); var review = Path.Combine(root, ".impeccable", "review"); Directory.CreateDirectory(review);
        await Expect(page.Locator(".sidebar-nav a[aria-current=page]")).ToHaveTextAsync("Painel");
        await Expect(page.Locator(".sidebar-nav a[aria-current=page]")).ToHaveCSSAsync("background-color","rgb(32, 59, 91)");
        await page.EvaluateAsync("window.scrollTo(0,0)"); await page.ScreenshotAsync(new() { Path = Path.Combine(review, "desktop.png"), FullPage = true, Animations = ScreenshotAnimations.Disabled });
        Assert.True(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= window.innerWidth"));
        await page.SetViewportSizeAsync(390, 844); await page.EvaluateAsync("window.scrollTo(0,0)"); await page.ScreenshotAsync(new() { Path = Path.Combine(review, "mobile.png"), FullPage = true, Animations = ScreenshotAnimations.Disabled }); Assert.True(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= window.innerWidth"));
        var nextTabs=page.GetByRole(AriaRole.Button,new(){Name="Mostrar próximas abas",Exact=true});await Expect(nextTabs).ToBeVisibleAsync();await nextTabs.ClickAsync();
        Assert.True(await page.Locator("#dashboard-tabs").EvaluateAsync<bool>("element=>element.scrollLeft>0"));
        await page.GetByRole(AriaRole.Tab,new(){Name="Equipe",Exact=true}).FocusAsync();await page.GetByRole(AriaRole.Tab,new(){Name="Equipe",Exact=true}).PressAsync("End");
        await Expect(page.GetByRole(AriaRole.Tab,new(){Name="Exportar Excel",Exact=true})).ToHaveAttributeAsync("aria-selected","true");await Expect(page.GetByRole(AriaRole.Tab,new(){Name="Exportar Excel",Exact=true})).ToBeInViewportAsync(new(){Ratio=.99f});
        await page.GetByRole(AriaRole.Tab,new(){Name="Exportar Excel",Exact=true}).PressAsync("Home");
        await Expect(page.GetByRole(AriaRole.Tab,new(){Name="Equipe",Exact=true})).ToHaveAttributeAsync("aria-selected","true");
        Assert.Empty(errors);
        await page.GotoAsync("/");await page.Locator(".dashboard-context > summary").ClickAsync();await page.Locator("#dashboard-team").SelectOptionAsync("2");await Expect(page.Locator("#dashboard-team")).ToHaveValueAsync("2");await Expect(page).ToHaveURLAsync(new Regex("team=2"));await Expect(page.Locator(".sidebar-nav a[aria-current=page]")).ToHaveTextAsync("Painel");await Expect(page.GetByRole(AriaRole.Link,new(){Name="Criar tarefa",Exact=true})).ToHaveAttributeAsync("href","tarefas?team=2&tab=new");
        await page.GetByRole(AriaRole.Link,new(){Name="Criar tarefa",Exact=true}).ClickAsync();await Expect(page.Locator("#tasks-team")).ToHaveValueAsync("2");
        await page.GetByLabel("Título da tarefa",new(){Exact=true}).FillAsync("Tarefa da equipe B");await page.GetByRole(AriaRole.Checkbox,new(){NameRegex=new Regex("Produto Equipe B")}).CheckAsync();await page.GetByRole(AriaRole.Button,new(){Name="Criar tarefa e publicar no GitHub"}).ClickAsync();await Expect(page.GetByRole(AriaRole.Status)).ToContainTextAsync("Tarefa criada");
        Assert.Equal(2,(await app.Read(d=>TimeOps.Domain.Result<int>.Success(d.Tasks.Single(t=>t.Title=="Tarefa da equipe B").TeamId))).Value);
        await page.GetByRole(AriaRole.Link,new(){Name="Apontar horas",Exact=true}).First.ClickAsync();await Expect(page.Locator("#hours-team")).ToHaveValueAsync("2");
        await page.GotoAsync("/tarefas?team=1");await Expect(page.Locator("#tasks-team")).ToHaveValueAsync("1");
        await using var member = await browser.NewContextAsync(new() { BaseURL = client.BaseAddress.ToString(), ExtraHTTPHeaders = new Dictionary<string, string> { { "X-Test-User", "2" } } }); var memberPage = await member.NewPageAsync(); await memberPage.GotoAsync("/planejamento"); await Expect(memberPage.GetByRole(AriaRole.Button, new() { Name = "Salvar calendário" })).ToHaveCountAsync(0); await memberPage.GotoAsync("/acessos"); await Expect(memberPage.GetByRole(AriaRole.Status)).ToContainTextAsync("administrador");
    }
    [Fact]
    public async Task Configura_funcoes_capacidades_ciclos_produtos_e_acessos()
    {
        await using var app = new TestApplication(); app.UseKestrel(0); using var client = app.CreateClient();
        using var playwright = await Playwright.CreateAsync(); await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true, ExecutablePath = Environment.GetEnvironmentVariable("E2E_BROWSER_PATH"), Args = ["--no-sandbox"] });
        await using var context = await browser.NewContextAsync(new() { BaseURL = client.BaseAddress!.ToString(), Locale = "pt-BR", ViewportSize = new() { Width = 1440, Height = 1000 }, ExtraHTTPHeaders = new Dictionary<string, string> { { "X-Test-User", "1" } } });
        var page = await context.NewPageAsync();
        await page.GotoAsync("/planejamento");
        await Expect(page.GetByRole(AriaRole.Navigation, new(){Name="Navegação principal"}).GetByRole(AriaRole.Link)).ToHaveCountAsync(5);
        await page.GetByLabel("Nova função").FillAsync("Rascunho preservado");
        var teamTab=page.GetByRole(AriaRole.Tab,new(){Name="Equipe",Exact=true});await teamTab.FocusAsync();await teamTab.PressAsync("ArrowRight");
        await Expect(page.GetByRole(AriaRole.Tab,new(){Name="Capacidade",Exact=true})).ToBeFocusedAsync();
        await Expect(page.Locator("#capacity-hours")).ToBeVisibleAsync();await Expect(page.Locator("#role-name")).ToHaveCountAsync(0);
        await page.GetByRole(AriaRole.Tab,new(){Name="Capacidade",Exact=true}).PressAsync("Home");
        await Expect(page.GetByLabel("Nova função")).ToHaveValueAsync("Rascunho preservado");
        await page.GetByLabel("Nova função").FillAsync("QA"); await page.GetByRole(AriaRole.Button, new() { Name = "Criar função", Exact = true }).ClickAsync(); await Expect(page.GetByRole(AriaRole.Status)).ToContainTextAsync("Função criada");
        await Capture(page,"planning");
        await page.GetByRole(AriaRole.Tab,new(){Name="Capacidade",Exact=true}).ClickAsync();await page.Locator("#capacity-person").SelectOptionAsync("2"); await page.Locator("#capacity-role").SelectOptionAsync("2"); await page.Locator("#capacity-sprint").SelectOptionAsync("1"); await page.GetByLabel("Horas por dia").FillAsync("1.5"); await page.GetByRole(AriaRole.Button, new() { Name = "Salvar capacidade" }).ClickAsync(); await Expect(page.GetByRole(AriaRole.Status)).ToContainTextAsync("Capacidade salva");
        await Capture(page,"planning-capacity");
        await page.GetByRole(AriaRole.Tab,new(){Name="Calendário",Exact=true}).ClickAsync();await Expect(page.Locator("#leave-start")).ToHaveValueAsync("2026-11-09");await page.GetByLabel("Pessoa ou equipe").SelectOptionAsync("2"); await page.GetByLabel("Início da folga").FillAsync("2026-11-10"); await page.GetByLabel("Fim da folga").FillAsync("2026-11-11"); await page.GetByRole(AriaRole.Button, new() { Name = "Cadastrar folga" }).ClickAsync(); await Expect(page.GetByRole(AriaRole.Status)).ToContainTextAsync("Folga cadastrada");
        await page.GetByRole(AriaRole.Tab,new(){Name="Ciclos e sprints",Exact=true}).ClickAsync();await page.GetByText("Novo ciclo", new() { Exact = true }).ClickAsync(); await page.GetByLabel("Nome do ciclo").FillAsync("Ciclo seguinte"); await page.GetByLabel("Quantidade de sprints").FillAsync("2"); await page.GetByLabel("Início da primeira sprint").FillAsync("2026-12-01"); await page.GetByLabel("Duração inicial em dias corridos").FillAsync("7"); await page.GetByRole(AriaRole.Button, new() { Name = "Preparar sprints" }).ClickAsync(); await page.GetByRole(AriaRole.Button, new() { Name = "Criar ciclo e sprints" }).ClickAsync(); await Expect(page.GetByRole(AriaRole.Status)).ToContainTextAsync("Ciclo e sprints criados");
        Assert.Equal(2, (await app.Read(d => TimeOps.Domain.Result<int>.Success(d.Sprints.Count(s => s.CycleId == d.Cycles.Single(c=>c.Name=="Ciclo seguinte").Id)))).Value);
        await page.GetByRole(AriaRole.Tab,new(){Name="Produtos",Exact=true}).ClickAsync();await page.GetByLabel("Nome do produto").FillAsync("Produto C"); await page.GetByLabel("Repositório", new() { Exact = true }).FillAsync("org/c"); await page.GetByLabel("Número do Project").FillAsync("3"); await page.GetByRole(AriaRole.Button, new() { Name = "Consultar GitHub" }).ClickAsync(); await page.GetByLabel("Todo", new() { Exact = true }).SelectOptionAsync("Proposed"); await page.GetByRole(AriaRole.Button, new() { Name = "Cadastrar produto" }).ClickAsync(); await Expect(page.GetByRole(AriaRole.Status)).ToContainTextAsync("Produto salvo");
        await page.GetByRole(AriaRole.Tab,new(){Name="Ciclos e sprints",Exact=true}).ClickAsync();await page.GetByRole(AriaRole.Button,new(){Name="Editar",Exact=true}).First.ClickAsync();await Expect(page.Locator("#edit-sprint-name")).ToBeFocusedAsync();await page.GetByRole(AriaRole.Button,new(){Name="Salvar sprint",Exact=true}).ClickAsync();await Expect(page.GetByRole(AriaRole.Status)).ToContainTextAsync("Sprint salva");await page.GetByRole(AriaRole.Button,new(){Name="Cancelar edição",Exact=true}).ClickAsync();
        await Capture(page,"planning-cycles");
        await page.GetByRole(AriaRole.Tab,new(){Name="Produtos",Exact=true}).ClickAsync();await page.GetByRole(AriaRole.Button,new(){Name="Editar produto e status"}).Last.ClickAsync();await Expect(page.Locator("#product-name")).ToBeFocusedAsync();await page.GetByRole(AriaRole.Button,new(){Name="Salvar produto e status"}).ClickAsync();await Expect(page.GetByRole(AriaRole.Status)).ToContainTextAsync("Produto salvo");await page.GetByRole(AriaRole.Button,new(){Name="Limpar formulário"}).ClickAsync();await Capture(page,"planning-products");
        await page.GotoAsync("/acessos"); await page.GetByRole(AriaRole.Button,new(){Name="Autorizar usuário",Exact=true}).ClickAsync();await page.GetByLabel("Login do GitHub").FillAsync("carla-test"); await page.GetByRole(AriaRole.Button, new() { Name = "Salvar acesso" }).ClickAsync(); await Expect(page.GetByRole(AriaRole.Status)).ToContainTextAsync("Acesso atualizado"); await page.GetByRole(AriaRole.Button,new(){Name="Editar acesso"}).Last.ClickAsync();await Expect(page.Locator("#access-login")).ToBeFocusedAsync();await page.GetByRole(AriaRole.Tab,new(){Name="Usuários",Exact=true}).ClickAsync();await Capture(page, "access");
        await page.SetViewportSizeAsync(390,844);
        var menu=page.GetByRole(AriaRole.Button,new(){Name="Menu",Exact=true});await Expect(menu).ToHaveAttributeAsync("aria-expanded","false");
        await Expect(page.GetByRole(AriaRole.Navigation,new(){Name="Navegação principal"})).ToBeHiddenAsync();
        await menu.ClickAsync();await Expect(menu).ToHaveAttributeAsync("aria-expanded","true");
        await menu.PressAsync("Escape");await Expect(menu).ToHaveAttributeAsync("aria-expanded","false");await Expect(menu).ToBeFocusedAsync();await menu.ClickAsync();
        await Capture(page,"menu");await page.SetViewportSizeAsync(390,844);
        await page.GetByRole(AriaRole.Link,new(){Name="Planejamento",Exact=true}).ClickAsync();await Expect(menu).ToHaveAttributeAsync("aria-expanded","false");
        Assert.True(await page.EvaluateAsync<int>("document.documentElement.scrollHeight")<1500,"Planejamento deve mostrar uma seção de cada vez no celular.");
        await using var anonymous = await browser.NewContextAsync(new() { BaseURL = client.BaseAddress.ToString(), ViewportSize = new() { Width = 1440, Height = 1000 } }); var login = await anonymous.NewPageAsync(); await login.GotoAsync("/login"); await Expect(login.GetByRole(AriaRole.Link, new() { Name = "Entrar com GitHub" })).ToBeVisibleAsync(); await Capture(login, "login");
    }
    private static async Task Capture(IPage page, string name)
    {
        var folder = Path.Combine(FindRoot(), ".impeccable", "review"); Directory.CreateDirectory(folder);
        await page.EvaluateAsync("window.scrollTo(0,0)"); await page.ScreenshotAsync(new() { Path = Path.Combine(folder, name + "-desktop.png"), FullPage = true, Animations = ScreenshotAnimations.Disabled });
        await page.SetViewportSizeAsync(390, 844); await page.EvaluateAsync("window.scrollTo(0,0)");
        if(await page.GetByRole(AriaRole.Tab,new(){Selected=true}).CountAsync()>0)await Expect(page.GetByRole(AriaRole.Tab,new(){Selected=true})).ToBeInViewportAsync(new(){Ratio=.99f});
        await page.ScreenshotAsync(new() { Path = Path.Combine(folder, name + "-mobile.png"), FullPage = true, Animations = ScreenshotAnimations.Disabled });
        Assert.True(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= window.innerWidth"), name + " excedeu a largura mobile");
        await page.SetViewportSizeAsync(1440, 1000);
    }
    private static string FindRoot() { var dir = new DirectoryInfo(AppContext.BaseDirectory); while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TimeOps.slnx"))) dir = dir.Parent; return dir!.FullName; }
}
