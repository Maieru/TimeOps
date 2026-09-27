using TimeOps.Application;
using TimeOps.Infrastructure;
using TimeOps.Web.Components;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();

var port = builder.Configuration.GetValue<int?>("TimeOps:Port") ?? 5191;
if (port is < 1024 or > 65535) throw new InvalidOperationException("A porta do TimeOps deve estar entre 1024 e 65535.");
builder.WebHost.UseUrls($"http://127.0.0.1:{port}");
builder.Services.Configure<AzureDevOpsOptions>(builder.Configuration.GetSection("AzureDevOps"));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddMemoryCache();
builder.Services.AddScoped<RuntimeConnection>();
builder.Services.AddScoped<IRuntimeConnection>(serviceProvider => serviceProvider.GetRequiredService<RuntimeConnection>());
builder.Services.AddSingleton<IConnectionStore>(new WindowsCredentialStore());
builder.Services.AddHttpClient<IDevOpsGateway, AzureDevOpsGateway>(client => client.Timeout = TimeSpan.FromSeconds(30));
builder.Services.AddScoped(serviceProvider => new DashboardService(
    serviceProvider.GetRequiredService<IDevOpsGateway>(),
    serviceProvider.GetRequiredService<IRuntimeConnection>(),
    serviceProvider.GetRequiredService<IConnectionStore>(),
    serviceProvider.GetRequiredService<TimeProvider>(),
    serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<AzureDevOpsOptions>>().CurrentValue.TimeZoneId));
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
