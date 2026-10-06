using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.JSInterop;
using TimeOps.Application;
using TimeOps.Infrastructure;
using TimeOps.Web.Components;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");
builder.Services.Configure<AzureDevOpsOptions>(builder.Configuration.GetSection("AzureDevOps"));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddMemoryCache();
builder.Services.AddScoped<DashboardNavigationState>();
builder.Services.AddScoped<RuntimeConnection>();
builder.Services.AddScoped<IRuntimeConnection>(services => services.GetRequiredService<RuntimeConnection>());
builder.Services.AddScoped<IConnectionStore>(services => new BrowserConnectionStore(
    (IJSInProcessRuntime)services.GetRequiredService<IJSRuntime>()));
builder.Services.AddScoped(_ => new HttpClient { Timeout = TimeSpan.FromSeconds(30) });
builder.Services.AddScoped<IDevOpsGateway, AzureDevOpsGateway>();
builder.Services.AddScoped<ITaskExportWriter, TaskExportWriter>();
builder.Services.AddScoped(services => new TaskExportService(
    services.GetRequiredService<IDevOpsGateway>(),
    services.GetRequiredService<TimeProvider>(),
    services.GetRequiredService<Microsoft.Extensions.Options.IOptions<AzureDevOpsOptions>>().Value.TimeZoneId));
builder.Services.AddScoped(services => new DashboardService(
    services.GetRequiredService<IDevOpsGateway>(),
    services.GetRequiredService<IRuntimeConnection>(),
    services.GetRequiredService<IConnectionStore>(),
    services.GetRequiredService<TimeProvider>(),
    services.GetRequiredService<Microsoft.Extensions.Options.IOptions<AzureDevOpsOptions>>().Value.TimeZoneId));

await builder.Build().RunAsync();
