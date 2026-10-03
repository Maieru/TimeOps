using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using System.Net;
using Xunit.Abstractions;
using HostProgram = TimeOps.E2E.Host.Program;

namespace TimeOps.E2E;

public abstract class CenarioE2E(ITestOutputHelper output) : IAsyncLifetime
{
    private WebApplicationFactory<HostProgram>? fabrica;
    private IPlaywright? playwright;
    private IBrowser? navegador;
    private IBrowserContext? contexto;
    private string pasta = "";
    private bool tracing;
    private bool initialized;
    private bool disposed;
    private readonly List<string> errosJavascript = [];
    protected IPage Pagina { get; private set; } = null!;
    protected AzureDevOpsMock DevOps { get; } = new();
    protected virtual bool Mobile => false;

    public async Task InitializeAsync()
    {
        var raiz = new DirectoryInfo(AppContext.BaseDirectory);
        while (raiz is not null && !File.Exists(Path.Combine(raiz.FullName, "TimeOps.slnx")))
            raiz = raiz.Parent;
        if (raiz is null)
            throw new InvalidOperationException("Raiz do projeto não encontrada.");

        pasta = Path.Combine(raiz.FullName, "artifacts", "e2e", GetType().Name, Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(pasta);
        output.WriteLine($"Artefatos deste cenário: {pasta}");
        var webRoot = Path.GetFullPath(Environment.GetEnvironmentVariable("E2E_WEB_ROOT")
            ?? Path.Combine(raiz.FullName, "artifacts", "e2e-site", "wwwroot"));
        if (!File.Exists(Path.Combine(webRoot, "index.html")))
            throw new InvalidOperationException("Publique o app antes dos testes: dotnet publish src/TimeOps.Web -c Release -o artifacts/e2e-site. Ou configure E2E_WEB_ROOT.");

        try
        {
            fabrica = new WebApplicationFactory<HostProgram>().WithWebHostBuilder(builder =>
                builder.UseEnvironment("Testing")
                    .ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0))
                    .ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                        new Dictionary<string, string?>
                        {
                            ["StaticFilesPath"] = webRoot,
                            ["Logging:LogLevel:Default"] = "Warning"
                        })));
            // Match the supplied base class: Kestrel uses a loopback port chosen by the OS.
            fabrica.UseKestrel(0);
            using var cliente = fabrica.CreateClient();

            playwright = await Playwright.CreateAsync();
            navegador = await playwright.Chromium.LaunchAsync(new()
            {
                Channel = Environment.GetEnvironmentVariable("E2E_CHANNEL"),
                Headless = false,
                SlowMo = int.TryParse(Environment.GetEnvironmentVariable("E2E_SLOWMO"), out var slowMo) ? slowMo : 300
            });
            contexto = await navegador.NewContextAsync(new()
            {
                BaseURL = cliente.BaseAddress!.ToString(),
                Locale = "pt-BR",
                TimezoneId = "America/Sao_Paulo",
                ViewportSize = new() { Width = Mobile ? 390 : 1280, Height = Mobile ? 844 : 720 },
                IsMobile = Mobile,
                HasTouch = Mobile,
                ServiceWorkers = ServiceWorkerPolicy.Block
            });
            contexto.SetDefaultTimeout(15_000);
            contexto.SetDefaultNavigationTimeout(30_000);
            // The REST dependency is mocked in the browser, before Blazor starts.
            _ = await contexto.RouteAsync("https://dev.azure.com/**", DevOps.ResponderAsync);
            await contexto.Tracing.StartAsync(new() { Screenshots = true, Snapshots = true, Sources = true });
            tracing = true;
            Pagina = await contexto.NewPageAsync();
            Pagina.PageError += (_, erro) => errosJavascript.Add(erro);
            _ = await Pagina.GotoAsync("/");
            await Expect(Pagina.GetByRole(AriaRole.Heading, new() { Name = "Conectar ao Azure DevOps" })).ToBeVisibleAsync();
            initialized = true;
        }
        catch
        {
            await DisposeAsync();
            throw;
        }
    }

    protected async Task ConectarAsync(bool lembrar = true)
    {
        await Pagina.GetByLabel("Organização", new() { Exact = true }).FillAsync(AzureDevOpsMock.Organizacao);
        await Pagina.GetByLabel("Personal Access Token", new() { Exact = true }).FillAsync(AzureDevOpsMock.Token);
        await Pagina.GetByRole(AriaRole.Checkbox).SetCheckedAsync(lembrar);
        await Pagina.GetByRole(AriaRole.Button, new() { Name = "Conectar", Exact = true }).ClickAsync();
        await Expect(Pagina.GetByLabel("Projeto", new() { Exact = true })).ToBeEnabledAsync();
    }

    protected async Task AbrirSprintAsync()
    {
        await ConectarAsync();
        _ = await Pagina.GetByLabel("Projeto", new() { Exact = true }).SelectOptionAsync("p1");
        await Expect(Pagina.GetByLabel("Equipe", new() { Exact = true })).ToBeEnabledAsync();
        _ = await Pagina.GetByLabel("Equipe", new() { Exact = true }).SelectOptionAsync("t1");
        await Expect(Pagina.GetByLabel("Sprint", new() { Exact = true })).ToBeEnabledAsync();
        _ = await Pagina.GetByLabel("Sprint", new() { Exact = true }).SelectOptionAsync("s1");
        await Expect(Pagina.GetByRole(AriaRole.Region, new() { Name = "Resumo da equipe" })).ToBeVisibleAsync();
        await Expect(Pagina.GetByRole(AriaRole.Button, new() { Name = "Atualizar", Exact = true })).ToBeEnabledAsync();
    }

    protected Task SelecionarVisaoAsync(string nome) => Pagina.GetByRole(AriaRole.Navigation,
        new() { Name = "Visões da sprint" }).GetByRole(AriaRole.Button, new() { Name = nome, Exact = true }).ClickAsync();

    public async Task DisposeAsync()
    {
        if (disposed)
            return;
        disposed = true;
        try
        {
            if (contexto is not null)
            {
                try
                {
                    if (Pagina is not null && !Pagina.IsClosed)
                        _ = await Pagina.ScreenshotAsync(new() { Path = Path.Combine(pasta, "pagina.png"), FullPage = true });
                }
                catch (PlaywrightException erro) { output.WriteLine($"Screenshot indisponível: {erro.Message}"); }
                finally
                {
                    if (tracing)
                    {
                        tracing = false;
                        await contexto.Tracing.StopAsync(new() { Path = Path.Combine(pasta, "trace.zip") });
                    }
                }
            }
        }
        finally
        {
            try
            {
                if (navegador is not null)
                    await navegador.CloseAsync();
            }
            finally
            {
                playwright?.Dispose();
                if (fabrica is not null)
                    await fabrica.DisposeAsync();
            }
        }
        if (initialized)
        {
            Assert.Empty(DevOps.RequisicoesInesperadas);
            Assert.Empty(errosJavascript);
        }
    }
}
