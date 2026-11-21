using System.Windows;
using NeoUninstall.Infrastructure;
using NeoUninstall.Models;
using NeoUninstall.Services;

namespace NeoUninstall.ViewModels;

public sealed class StartupViewModel : AuditPageViewModel<StartupEntry>
{
    public override string Title => "Startup";
    public override string Glyph => "\uE7E8";
    public override bool HasSystemFilter => false;
    public override string ShowSystemLabel => "";
    public string ToggleLabel => Selected?.IsEnabled == false ? "Enable" : "Disable";
    public RelayCommand ToggleCommand { get; }

    public StartupViewModel() { ToggleCommand = new(_ => Toggle(), _ => Selected != null); }

    protected override List<StartupEntry> Collect(AppAssociator a, CancellationToken ct) => StartupService.Scan(a, ct);
    protected override bool IsSystem(StartupEntry e) => false;
    protected override bool IsFlagged(StartupEntry e) => e.TargetMissing;
    protected override string SearchText(StartupEntry e) => $"{e.Name} {e.Publisher} {e.Command}";
    protected override void OnSelected(StartupEntry? e) => Raise(nameof(ToggleLabel));

    private void Toggle()
    {
        var e = Selected; if (e == null) return;
        bool enable = !e.IsEnabled;
        var ok = MessageBox.Show(enable
            ? $"Enable \"{e.Name}\" at Windows startup?"
            : $"Disable \"{e.Name}\" at Windows startup?\n\nThe entry is not deleted — you can enable it again at any time.",
            enable ? "Enable startup entry" : "Disable startup entry", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (ok != MessageBoxResult.Yes) return;
        try
        {
            StartupService.SetEnabled(e, enable);
            e.IsEnabled = enable; Raise(nameof(ToggleLabel));
            Logger.Op($"Startup entry {(enable ? "ENABLED" : "DISABLED")}: {e.Name} ({e.Source}, {e.Command})");
        }
        catch (UnauthorizedAccessException) { MessageBox.Show("Changing this entry requires administrator rights. Restart NeoUninstall as administrator (Leftovers page has a button for it).", "NeoUninstall"); }
        catch (Exception ex) { Logger.Error($"Startup toggle failed for {e.Name}", ex); MessageBox.Show("Could not change the entry:\n" + ex.Message, "NeoUninstall"); }
    }
}

public sealed class TasksViewModel : AuditPageViewModel<TaskEntry>
{
    public override string Title => "Scheduled Tasks";
    public override string Glyph => "\uE787";
    public override string ShowSystemLabel => "Show Microsoft tasks";
    protected override List<TaskEntry> Collect(AppAssociator a, CancellationToken ct) => TaskScanner.Scan(a, ct);
    protected override bool IsSystem(TaskEntry t) => t.IsMicrosoft;
    protected override bool IsFlagged(TaskEntry t) => t.Flagged;
    protected override string SearchText(TaskEntry t) => $"{t.Name} {t.TaskPath} {t.ExePath} {t.Author}";
}

public sealed class ServicesViewModel : AuditPageViewModel<ServiceEntry>
{
    public override string Title => "Services";
    public override string Glyph => "\uE713";
    public override string ShowSystemLabel => "Show Windows / Microsoft services";
    protected override List<ServiceEntry> Collect(AppAssociator a, CancellationToken ct) => ServiceScanner.Scan(a, ct);
    protected override bool IsSystem(ServiceEntry s) => s.IsSystem;
    protected override bool IsFlagged(ServiceEntry s) => s.Flagged;
    protected override string SearchText(ServiceEntry s) => $"{s.DisplayName} {s.Name} {s.Publisher} {s.ExePath}";
}
