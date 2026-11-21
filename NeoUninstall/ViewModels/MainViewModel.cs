using System.Collections.ObjectModel;
using System.Windows;
using NeoUninstall.Infrastructure;
using NeoUninstall.Models;
using NeoUninstall.Services;

namespace NeoUninstall.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private PageViewModel _current;
    public DashboardViewModel Dashboard { get; } = new();
    public SoftwareViewModel Software { get; } = new();
    public LeftoversViewModel Leftovers { get; } = new();
    public StartupViewModel Startup { get; } = new();
    public TasksViewModel Tasks { get; } = new();
    public ServicesViewModel Services { get; } = new();
    public BackupsViewModel Backups { get; } = new();
    public ObservableCollection<PageViewModel> Pages { get; }

    public PageViewModel Current
    {
        get => _current;
        set { if (Set(ref _current, value) && value is BackupsViewModel b) b.Refresh(); }
    }

    public MainViewModel()
    {
        Pages = new() { Dashboard, Software, Leftovers, Startup, Tasks, Services, Backups };
        _current = Dashboard;
        Dashboard.ScanCompleted += Software.SetApps;
        Dashboard.ExtraStage = async (apps, ct) =>
        {
            await Leftovers.ScanAsync(apps, null, ct);
            ct.ThrowIfCancellationRequested();
            Dashboard.SetStatus("Auditing startup, tasks and services…");
            await Startup.LoadAsync(apps, ct);
            await Tasks.LoadAsync(apps, ct);
            await Services.LoadAsync(apps, ct);
        };
        Leftovers.ResultsChanged += Dashboard.SetLeftoverSummary;
        Leftovers.CleanupFinished += Backups.Refresh;
        Startup.Summary += (t, f) => Dashboard.SetAuditSummary("startup", t, f);
        Tasks.Summary += (t, f) => Dashboard.SetAuditSummary("tasks", t, f);
        Services.Summary += (t, f) => Dashboard.SetAuditSummary("services", t, f);
        Software.DeepUninstallRequested += OnDeepUninstall;
    }

    public Task InitializeAsync() => Dashboard.RunScanAsync();

    private async void OnDeepUninstall(InstalledApp app)
    {
        try
        {
            Dashboard.SetStatus($"Running the official uninstaller for {app.Name}…");
            Logger.Op($"Deep uninstall started: {app.Name}");
            var code = await Task.Run(() => UninstallerRunner.RunAndWait(app.UninstallString));
            Logger.Op($"Official uninstaller for {app.Name} exited with code {code}");

            var apps = await Task.Run(() => new InstalledAppScanner().Scan(CancellationToken.None));
            Software.SetApps(apps);
            bool still = apps.Any(a => string.Equals(a.RegistryPath, app.RegistryPath, StringComparison.OrdinalIgnoreCase));
            if (still)
                MessageBox.Show("The uninstall entry for this application is still present — the uninstaller may have been cancelled, " +
                                "or it is still running in the background. A general leftover scan will run instead of a targeted one.",
                                "Deep uninstall");
            Current = Leftovers;
            await Leftovers.ScanAsync(apps, still ? null : app);
            Dashboard.SetStatus("Ready");
        }
        catch (Exception ex) { Logger.Error("Deep uninstall failed", ex); Dashboard.SetStatus("Deep uninstall failed — see log"); }
    }
}
