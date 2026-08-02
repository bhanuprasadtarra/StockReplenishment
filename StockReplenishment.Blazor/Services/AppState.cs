namespace StockReplenishment.Blazor.Services;

// Static on purpose - this is a Blazor Server app so "static" effectively means
// per-process, not per-user. Fine for a role-selection demo with one circuit at a
// time; a real multi-user app would need this scoped per-circuit instead (e.g. via
// a scoped DI service or auth cookie), not shared across every connected browser.
public static class AppState
{
    public static string? CurrentRole { get; set; }
    public static string CurrentUser { get; set; } = "";

    public static void SetRole(string role)
    {
        CurrentRole = role;
        CurrentUser = $"{role} 1";
    }

    public static string GetRole() => CurrentRole ?? "None";

    public static bool IsWorker() => CurrentRole == "Worker";
    public static bool IsReviewer() => CurrentRole == "Reviewer";

    public static void ClearRole()
    {
        CurrentRole = null;
        CurrentUser = "";
    }
}
