namespace TimeOps.Web.Components;

public enum DashboardView { Team, Person, History }

// Shared by the interactive layout and the dashboard within one browser circuit.
public sealed class DashboardNavigationState
{
    public DashboardView Current { get; private set; } = DashboardView.Team;
    public string? ProjectName { get; private set; }
    public string? TeamName { get; private set; }
    public string? SprintName { get; private set; }

    public event Action<DashboardView>? ViewChanged;
    public event Action? Changed;

    public void Select(DashboardView view)
    {
        if (Current == view) return;
        Current = view;
        ViewChanged?.Invoke(view);
        Changed?.Invoke();
    }

    public void SetContext(string? project, string? team, string? sprint)
    {
        if (ProjectName == project && TeamName == team && SprintName == sprint) return;
        ProjectName = project;
        TeamName = team;
        SprintName = sprint;
        Changed?.Invoke();
    }
}
