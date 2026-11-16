using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TimeOps.Application;
using TimeOps.Domain;
using TimeOps.Infrastructure;
using TimeOps.Web;
using TimeOps.Web.Components;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseStaticWebAssets();
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.GetCultureInfo("pt-BR");
CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo("pt-BR");
builder.Services.AddRazorComponents().AddInteractiveServerComponents(o => o.DetailedErrors = builder.Configuration.GetValue<bool>("DetailedErrors"));
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<DashboardNavigationState>();
builder.Services.Configure<GitHubOptions>(builder.Configuration.GetSection("GitHub"));
builder.Services.AddDbContextFactory<PlanningDbContext>(o => o.UseNpgsql(builder.Configuration.GetConnectionString("TimeOps")
    ?? "Host=localhost;Database=timeops;Username=timeops"));
builder.Services.AddScoped<IPlanningStore, EfPlanningStore>();
builder.Services.AddScoped<ICurrentActor, CurrentActor>();
builder.Services.AddScoped<PlanningService>();
builder.Services.AddScoped<PlanningReports>();
builder.Services.AddScoped<GitHubSynchronization>();
builder.Services.AddSingleton<GitHubAppToken>();
builder.Services.AddScoped<IGitHubProjectsGateway, GitHubProjectsGateway>();
builder.Services.AddScoped<ITaskExportWriter, TaskExportWriter>();
builder.Services.AddHttpClient("GitHub", client =>
{
    client.BaseAddress = new Uri("https://api.github.com/"); client.Timeout = TimeSpan.FromSeconds(30);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("TimeOps/2.0");
    client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
});
var protection = builder.Services.AddDataProtection().SetApplicationName("TimeOps");
if (builder.Configuration["DataProtection:KeyPath"] is { Length: > 0 } keyPath)
    protection.PersistKeysToFileSystem(new DirectoryInfo(keyPath));
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.LoginPath = "/login"; o.AccessDeniedPath = "/login?login=denied";
        o.Cookie.Name = "TimeOps.Session"; o.Cookie.HttpOnly = true; o.Cookie.SameSite = SameSiteMode.Lax;
        o.Cookie.SecurePolicy = builder.Environment.IsProduction() ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
        o.ExpireTimeSpan = TimeSpan.FromHours(8); o.SlidingExpiration = true;
        o.Events.OnValidatePrincipal = async context =>
        {
            var id = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
            var store = context.HttpContext.RequestServices.GetRequiredService<IPlanningStore>();
            var result = await store.ReadAsync(d => Result<bool>.Success(d.Users.Any(u => u.Id == id && u.IsActive)), context.HttpContext.RequestAborted);
            if (result.IsFailure || !result.Value) context.RejectPrincipal();
        };
    })
    .AddOAuth("GitHub", o =>
    {
        o.ClientId = string.IsNullOrWhiteSpace(builder.Configuration["GitHub:ClientId"]) ? "unconfigured" : builder.Configuration["GitHub:ClientId"]!;
        o.ClientSecret = string.IsNullOrWhiteSpace(builder.Configuration["GitHub:ClientSecret"]) ? "unconfigured" : builder.Configuration["GitHub:ClientSecret"]!;
        o.CallbackPath = "/signin-github"; o.AuthorizationEndpoint = "https://github.com/login/oauth/authorize";
        o.TokenEndpoint = "https://github.com/login/oauth/access_token"; o.UserInformationEndpoint = "https://api.github.com/user";
        o.UsePkce = true; o.SaveTokens = false;
        o.Events = new OAuthEvents
        {
            OnCreatingTicket = async context =>
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, context.Options.UserInformationEndpoint);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", context.AccessToken);
                request.Headers.UserAgent.ParseAdd("TimeOps/2.0"); request.Headers.Accept.ParseAdd("application/vnd.github+json");
                using var response = await context.Backchannel.SendAsync(request, context.HttpContext.RequestAborted);
                if (!response.IsSuccessStatusCode) { context.Fail("Não foi possível consultar a conta do GitHub."); return; }
                using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(context.HttpContext.RequestAborted));
                var profile = json.RootElement;
                var id = profile.GetProperty("id").ToString();
                var login = profile.GetProperty("login").GetString()!;
                var name = profile.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString()! : login;
                var result = await context.HttpContext.RequestServices.GetRequiredService<IPlanningStore>().ChangeAsync(d =>
                {
                    var user = d.Users.SingleOrDefault(u => u.Id == id && u.IsActive);
                    if (user is null) return Result<bool>.Success(false);
                    user.Login = login; user.Name = name; return Result<bool>.Success(true);
                }, context.HttpContext.RequestAborted);
                if (result.IsFailure || !result.Value) { context.Fail("Conta sem acesso ao TimeOps."); return; }
                context.Identity!.AddClaim(new(ClaimTypes.NameIdentifier, id)); context.Identity.AddClaim(new(ClaimTypes.Name, name));
            },
            OnRemoteFailure = context => { context.HandleResponse(); context.Response.Redirect("/login?login=denied"); return Task.CompletedTask; }
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddHostedService<GitHubWorker>();
var app = builder.Build();
var forwarded = new Microsoft.AspNetCore.Builder.ForwardedHeadersOptions
{
    ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto
};
foreach (var address in app.Configuration.GetSection("ReverseProxy:KnownProxies").Get<string[]>() ?? [])
    if (System.Net.IPAddress.TryParse(address, out var proxy)) forwarded.KnownProxies.Add(proxy);
app.UseForwardedHeaders(forwarded);
if (!app.Environment.IsDevelopment()) { app.UseExceptionHandler("/Error"); app.UseHsts(); }
app.UseStaticFiles();
app.MapStaticAssets();
app.UseAuthentication(); app.UseAuthorization(); app.UseAntiforgery();
app.MapGet("/entrar", (IOptions<GitHubOptions> options) => options.Value.ClientId.Length == 0
    ? Results.Redirect("/login?login=configuration")
    : Results.Challenge(new AuthenticationProperties { RedirectUri = "/" }, ["GitHub"]));
app.MapPost("/sair", async (HttpContext context, Microsoft.AspNetCore.Antiforgery.IAntiforgery antiforgery) =>
{
    await antiforgery.ValidateRequestAsync(context); await context.SignOutAsync(); return Results.LocalRedirect("/");
}).RequireAuthorization();
app.MapPost("/webhooks/github", WebhookEndpoint.HandleAsync);
app.MapGet("/exportar", async (int teamId, int sprintId, DateOnly from, DateOnly to, string? author,
    PlanningReports reports, ITaskExportWriter writer, CancellationToken ct) =>
{
    var result = await reports.ExportAsync(teamId, sprintId, from, to, ct);
    if (result.IsFailure) return Results.Problem(result.Error!.Message, statusCode: result.Error.Category == ErrorCategory.Authorization ? 403 : 400);
    var rows = result.Value.ForAuthor(string.IsNullOrEmpty(author) ? null : author);
    if (rows.Count == 0) return Results.Problem("Nenhum apontamento no período selecionado.", statusCode: 400);
    var file = writer.Write(rows);
    return file.IsFailure ? Results.Problem(file.Error!.Message, statusCode: 400)
        : Results.File(file.Value, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", TaskExportService.FileName(result.Value));
}).RequireAuthorization();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapGet("/ready", async (IDbContextFactory<PlanningDbContext> factory, CancellationToken ct) =>
{
    try { await using var db = await factory.CreateDbContextAsync(ct); if (!await db.Database.CanConnectAsync(ct)) return Results.StatusCode(503); _ = await db.Set<PlanningTeam>().AnyAsync(ct); return Results.Ok(); }
    catch (Exception ex) when (ex is not OperationCanceledException) { return Results.StatusCode(503); }
});
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
await using (var scope = app.Services.CreateAsyncScope())
{
    var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<PlanningDbContext>>();
    await using var db = await factory.CreateDbContextAsync();
    if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup") || args.Contains("--migrate")) await db.Database.MigrateAsync();
    if (args.Contains("--migrate")) return;
    var bootstrap = app.Configuration["GitHub:BootstrapAdminId"];
    if (!string.IsNullOrWhiteSpace(bootstrap))
    {
        var initialized = await scope.ServiceProvider.GetRequiredService<IPlanningStore>().ChangeAsync(d =>
        {
            // Bootstrap only an empty installation; never override subsequent access revocation.
            if (d.Users.Count == 0) d.Users.Add(new() { Id = bootstrap, Name = "Administrador", IsAdmin = true });
            return Result<bool>.Success(true);
        });
        if (initialized.IsFailure) throw new InvalidOperationException(initialized.Error!.Message);
    }
}
await app.RunAsync();
public partial class Program;
