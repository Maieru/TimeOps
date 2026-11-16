using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TimeOps.Application;
using TimeOps.Domain;
using TimeOps.Infrastructure;
namespace TimeOps.E2E;

public sealed class TestApplication : WebApplicationFactory<Program>
{
    private readonly string connectionString = $"Data Source=timeops-{Guid.NewGuid():N};Mode=Memory;Cache=Shared;Default Timeout=30";
    private readonly SqliteConnection? keepAlive;
    private readonly string? postgresConnection;
    public TestGitHub GitHub { get; } = new();
    public const string WebhookSecret = "synthetic-webhook-secret-for-tests-only";
    public TestApplication()
    {
        var configured = Environment.GetEnvironmentVariable("TIMEOPS_TEST_POSTGRES");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            var schema = "timeops_test_" + Guid.NewGuid().ToString("N");
            using var connection = new Npgsql.NpgsqlConnection(configured); connection.Open();
            using var command = new Npgsql.NpgsqlCommand("CREATE SCHEMA " + schema, connection); command.ExecuteNonQuery();
            var options = new Npgsql.NpgsqlConnectionStringBuilder(configured) { SearchPath = schema }; postgresConnection = options.ConnectionString;
        }
        else { keepAlive = new(connectionString); keepAlive.Open(); }
    }
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DetailedErrors"] = "true",
            ["GitHub:WorkerEnabled"] = "false",
            ["GitHub:BootstrapAdminId"] = "",
            ["GitHub:Organization"] = "org",
            ["GitHub:InstallationId"] = "42",
            ["GitHub:WebhookSecret"] = WebhookSecret,
            ["Database:MigrateOnStartup"] = "false",
            ["Logging:LogLevel:Default"] = "Error"
        }));
        builder.ConfigureServices(s =>
        {
            s.RemoveAll<Microsoft.EntityFrameworkCore.Infrastructure.IDbContextOptionsConfiguration<PlanningDbContext>>(); s.RemoveAll<IDbContextFactory<PlanningDbContext>>(); s.RemoveAll<DbContextOptions<PlanningDbContext>>(); s.RemoveAll<PlanningDbContext>();
            s.AddDbContextFactory<PlanningDbContext>(o => { if (postgresConnection is null) o.UseSqlite(connectionString); else o.UseNpgsql(postgresConnection); });
            s.RemoveAll<IGitHubProjectsGateway>(); s.AddSingleton<IGitHubProjectsGateway>(GitHub);
            s.RemoveAll<TimeProvider>(); s.AddSingleton<TimeProvider>(new FixedClock());
            s.AddAuthentication().AddScheme<AuthenticationSchemeOptions, TestAuthentication>("Testing", _ => { });
            s.PostConfigure<AuthenticationOptions>(o => { o.DefaultAuthenticateScheme = "Testing"; o.DefaultChallengeScheme = "Testing"; o.DefaultForbidScheme = "Testing"; });
            s.AddHostedService<TestSeed>();
        });
    }
    protected override void Dispose(bool disposing) { base.Dispose(disposing); if (disposing) keepAlive?.Dispose(); }
    public async Task<Result<T>> Read<T>(Func<PlanningData, Result<T>> query) { await using var scope = Services.CreateAsyncScope(); return await scope.ServiceProvider.GetRequiredService<IPlanningStore>().ReadAsync(query); }
    public async Task Sync() { await using var scope = Services.CreateAsyncScope(); var r = await scope.ServiceProvider.GetRequiredService<GitHubSynchronization>().RunAsync(); Assert.True(r.IsSuccess, r.Error?.Message); }
    private sealed class FixedClock : TimeProvider { public override DateTimeOffset GetUtcNow() => new(2026, 11, 10, 1, 0, 0, TimeSpan.Zero); }
}

public sealed class TestAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var id = Request.Headers["X-Test-User"].ToString();
        if (id.Length == 0) return Task.FromResult(AuthenticateResult.NoResult());
        var identity = new ClaimsIdentity([new(ClaimTypes.NameIdentifier, id), new(ClaimTypes.Name, id == "1" ? "Ana de teste" : "Bruno de teste")], Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(new(new ClaimsPrincipal(identity), Scheme.Name)));
    }
}
public sealed class TestSeed(IDbContextFactory<PlanningDbContext> factory, IPlanningStore store) : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        await using (var db = await factory.CreateDbContextAsync(ct)) { if (db.Database.IsNpgsql()) await db.Database.MigrateAsync(ct); else await db.Database.EnsureCreatedAsync(ct); }
        var result = await store.ChangeAsync(d =>
        {
            d.Users = [new() { Id = "1", Name = "Ana de teste", Login = "ana-test", IsAdmin = true }, new() { Id = "2", Name = "Bruno de teste", Login = "bruno-test" }, new() { Id = "3", Name = "Outra equipe", Login = "other" }];
            d.Teams = [new() { Id = 1, Name = "Equipe de teste" }];
            d.Memberships = [new() { Id = 1, TeamId = 1, UserId = "1", Access = TeamAccess.Manager }, new() { Id = 2, TeamId = 1, UserId = "2" }];
            d.Roles = [new() { Id = 1, TeamId = 1, Name = "Desenvolvimento" }]; d.Cycles = [new() { Id = 1, TeamId = 1, Name = "Ciclo de teste" }];
            d.Sprints = [new() { Id = 1, TeamId = 1, CycleId = 1, Name = "Sprint de teste", Start = new(2026, 11, 2), End = new(2026, 11, 13) }];
            d.Capacities = [new() { Id = 1, TeamId = 1, UserId = "1", RoleId = 1, SprintId = 1, HoursPerDay = 6 }, new() { Id = 2, TeamId = 1, UserId = "2", RoleId = 1, SprintId = 1, HoursPerDay = 6 }];
            d.Products = [new() { Id = 1, TeamId = 1, Name = "Produto A", Repository = "org/a", ProjectNodeId = "PA", ProjectNumber = 1, ProjectUrl = "https://github.com/orgs/org/projects/1", StatusFieldId = "status", StatusMappingJson = "{\"todo\":\"Proposed\"}" }, new() { Id = 2, TeamId = 1, Name = "Produto B", Repository = "org/b", ProjectNodeId = "PB", ProjectNumber = 2, ProjectUrl = "https://github.com/orgs/org/projects/2", StatusFieldId = "status", StatusMappingJson = "{\"todo\":\"Proposed\"}" }];
            d.Teams.Add(new(){Id=2,Name="Equipe B"});
            d.Memberships.Add(new(){Id=3,TeamId=2,UserId="1",Access=TeamAccess.Manager});
            d.Memberships.Add(new(){Id=4,TeamId=2,UserId="2"});
            d.Cycles.Add(new(){Id=2,TeamId=2,Name="Ciclo B"});
            d.Sprints.Add(new(){Id=2,CycleId=2,TeamId=2,Name="Sprint B",Start=new(2026,11,2),End=new(2026,11,13)});
            d.Products.Add(new(){Id=3,TeamId=2,Name="Produto Equipe B",Repository="org/team-b",ProjectNodeId="PC",ProjectNumber=3,ProjectUrl="https://github.com/orgs/org/projects/3",StatusFieldId="status",StatusMappingJson="{\"todo\":\"Proposed\"}"});
            return Result<bool>.Success(true);
        }, ct);
        if (result.IsFailure) throw new InvalidOperationException(result.Error!.Message);
    }
    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
public sealed class TestGitHub : IGitHubProjectsGateway
{
    private readonly Dictionary<string, GitHubIssue> issues = []; public int Created { get; private set; }
    public Task<Result<AppUser>> FindUserAsync(string login, CancellationToken ct) => Task.FromResult(Result<AppUser>.Success(new() { Id = "4", Login = login, Name = login }));
    public Task<Result<GitHubProjectInfo>> InspectAsync(string repo, int number, CancellationToken ct) => Task.FromResult(Result<GitHubProjectInfo>.Success(new("repo-" + repo, "P" + number, $"https://github.com/orgs/org/projects/{number}", "status", new Dictionary<string, string> { { "todo", "Todo" } })));
    public Task<Result<GitHubIssue?>> FindIssueAsync(string repo, Guid id, CancellationToken ct) => Task.FromResult(Result<GitHubIssue?>.Success(null));
    public Task<Result<GitHubIssue>> CreateIssueAsync(string repo, string title, string body, Guid id, CancellationToken ct) { Created++; var issue = new GitHubIssue("I-" + id, 1, "https://github.com/" + repo + "/issues/1", title, ["2"]); issues[issue.NodeId] = issue; return Task.FromResult(Result<GitHubIssue>.Success(issue)); }
    public Task<Result<string>> AddToProjectAsync(string project, string issue, CancellationToken ct) => Task.FromResult(Result<string>.Success("ITEM-" + issue));
    public Task<Result<GitHubExecution>> ReadExecutionAsync(PlanningProduct p, string id, CancellationToken ct) => Task.FromResult(Result<GitHubExecution>.Success(new(issues[id], "ITEM-" + id, "todo", "Todo", false)));
}
