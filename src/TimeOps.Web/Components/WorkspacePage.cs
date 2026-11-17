using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using TimeOps.Application;
using TimeOps.Domain;
namespace TimeOps.Web.Components;

public abstract class WorkspacePage : ComponentBase
{
    [Inject] protected PlanningService Planning { get; set; } = null!;
    [Inject] protected DashboardNavigationState Navigation { get; set; } = null!;
    [Inject] protected NavigationManager WorkspaceRouter { get; set; } = null!;
    [Inject] protected TimeProvider WorkspaceClock { get; set; } = null!;
    [SupplyParameterFromQuery(Name = "team")] public int? RequestedTeamId { get; set; }
    protected int TeamId { get; set; }
    private bool initialized;
    private int? routeTeam;
    private int loadGeneration;
    private Func<ElementReference>? focusTarget;
    protected PlanningWorkspace? Workspace;
    protected PlanningData Data => Workspace?.Data ?? new();
    protected bool Busy;
    protected string? Error;
    protected string? Message;
    protected bool Manager => Workspace is not null && (Workspace.User.IsAdmin || Data.Memberships.Any(m => m.TeamId == TeamId && m.UserId == Workspace.User.Id && m.Access == TeamAccess.Manager));
    protected override async Task OnParametersSetAsync()
    {
        if (initialized && routeTeam == RequestedTeamId) return;
        initialized = true; routeTeam = RequestedTeamId; TeamId = RequestedTeamId ?? Navigation.SelectedTeamId ?? 0;
        await Reload();
        if (Workspace is not null && routeTeam == RequestedTeamId) await OnContextLoadedAsync();
    }
    protected virtual Task OnContextLoadedAsync() => Task.CompletedTask;
    protected async Task Reload()
    {
        var generation = ++loadGeneration;
        Workspace = null;
        var result = await Planning.WorkspaceAsync();
        if (generation != loadGeneration) return;
        if (result.IsFailure) { Error = result.Error!.Message; Workspace = null; return; }
        Workspace = result.Value;
        if (!Workspace.Teams.Any(t => t.Id == TeamId))
        {
            if (RequestedTeamId.HasValue) { TeamId = 0; Error = "Esta equipe não está disponível para seu acesso. Selecione outra equipe."; }
            else TeamId = Workspace.Teams.FirstOrDefault()?.Id ?? 0;
        }
        if (TeamId > 0) Navigation.SelectTeam(TeamId);
        Navigation.SetContext("GitHub Projects", Data.Teams.SingleOrDefault(t => t.Id == TeamId)?.Name, null);
    }
    protected async Task Run<T>(Func<Task<Result<T>>> action, string success)
    {
        if (Busy) return;
        Busy = true; Error = null; Message = null;
        try { var result = await action(); if (result.IsFailure) Error = result.Error!.Message; else { Message = success; await Reload(); } }
        catch (Exception) { Error = "Não foi possível concluir a operação. Atualize a página e tente novamente."; }
        finally { Busy = false; }
    }
    protected Task TeamChanged(ChangeEventArgs e)
    {
        var selected = int.TryParse(e.Value?.ToString(), out var id) ? id : 0;
        Error = null; Message = null;
        var uri = WorkspaceRouter.GetUriWithQueryParameter("team", selected);
        if (uri != WorkspaceRouter.Uri) WorkspaceRouter.NavigateTo(uri, replace: true);
        return Task.CompletedTask;
    }
    protected DateTimeOffset TeamLocalTime(DateTimeOffset value) => TimeZoneInfo.ConvertTime(value,
        TimeZoneInfo.FindSystemTimeZoneById(Data.Teams.SingleOrDefault(t => t.Id == TeamId)?.TimeZoneId ?? "America/Sao_Paulo"));
    protected DateOnly LocalToday => DateOnly.FromDateTime(TeamLocalTime(WorkspaceClock.GetUtcNow()).DateTime);
    protected void FocusAfterRender(Func<ElementReference> target) => focusTarget = target;
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (focusTarget is not { } target) return;
        focusTarget = null;
        try { await target().FocusAsync(); } catch (JSException) { }
    }
    protected string UserName(string id) => Data.Users.SingleOrDefault(u => u.Id == id)?.Name ?? id;
    protected static string H(decimal? value) => value is null ? "—" : value.Value.ToString("N2") + " h";
}
