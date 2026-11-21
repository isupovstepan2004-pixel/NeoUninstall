using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;
using System.Windows;
using NeoUninstall.Infrastructure;
using NeoUninstall.Models;
using NeoUninstall.Services;
using NeoUninstall.Views;

namespace NeoUninstall.ViewModels;

public sealed class LeftoversViewModel : PageViewModel
{
    private readonly LeftoverEngine _engine = new();
    private readonly CleanupService _cleanup = new();
    private List<LeftoverItem> _all = new();
    private CancellationTokenSource? _cts;
    private bool _busy; private string _status = "No leftover scan yet. Run a scan to look for leftovers.";
    private AppGroup? _group; private LeftoverItem? _item;

    public override string Title => "Leftovers";
    public override string Glyph => "\uE74D";
    public event Action<int, long>? ResultsChanged;
    public event Action? CleanupFinished;

    public ObservableCollection<AppGroup> Groups { get; } = new();
    public bool IsBusy { get => _busy; private set => Set(ref _busy, value); }
    public string Status { get => _status; private set => Set(ref _status, value); }
    public AppGroup? SelectedGroup { get => _group; set => Set(ref _group, value); }
    public LeftoverItem? SelectedItem { get => _item; set => Set(ref _item, value); }
    public int SelectedCount => _all.Count(i => i.IsSelected);
    public string DeleteLabel => $"Review & delete ({SelectedCount})…";
    public bool NeedsAdmin { get; } = !new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

    public RelayCommand ScanCommand { get; }
    public RelayCommand CancelCommand { get; }
    public RelayCommand SelectHighCommand { get; }
    public RelayCommand ClearSelectionCommand { get; }
    public RelayCommand CleanCommand { get; }
    public RelayCommand RestartAdminCommand { get; }

    public LeftoversViewModel()
    {
        ScanCommand = new(async _ => await ScanAsync(), _ => !IsBusy);
        CancelCommand = new(_ => _cts?.Cancel(), _ => IsBusy);
        SelectHighCommand = new(_ => { foreach (var i in _all) i.IsSelected = i.Confidence == Confidence.High; }, _ => !IsBusy);
        ClearSelectionCommand = new(_ => { foreach (var i in _all) i.IsSelected = false; }, _ => !IsBusy);
        CleanCommand = new(async _ => await CleanAsync(), _ => !IsBusy && SelectedCount > 0);
        RestartAdminCommand = new(_ => RestartAsAdmin());
    }

    public async Task ScanAsync(List<InstalledApp>? apps = null, InstalledApp? target = null, CancellationToken external = default)
    {
        if (IsBusy) return;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(external); IsBusy = true;
        Status = target != null ? $"Looking for leftovers of {target.Name}…" : "Scanning for leftovers…";
        var token = _cts.Token;
        try
        {
            var progress = new Progress<string>(s => Status = s);
            var list = await Task.Run(() =>
            {
                var installed = apps ?? new InstalledAppScanner().Scan(token);
                return _engine.Scan(installed, target, progress, token);
            }, token);
            foreach (var i in list) i.PropertyChanged += OnItemChanged;
            _all = list; Rebuild();
            Status = list.Count == 0 ? "No leftovers with sufficient evidence were found." : $"{list.Count} item(s) in {Groups.Count} group(s). Nothing is selected — review each item first.";
        }
        catch (OperationCanceledException) { Status = "Leftover scan cancelled"; }
        catch (Exception ex) { Status = "Leftover scan failed — see log"; Logger.Error("Leftover scan failed", ex); }
        finally { IsBusy = false; }
    }

    private void OnItemChanged(object? s, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LeftoverItem.IsSelected)) { Raise(nameof(SelectedCount)); Raise(nameof(DeleteLabel)); }
    }

    private void Rebuild()
    {
        Groups.Clear();
        foreach (var g in AppGroup.Build(_all)) Groups.Add(g);
        SelectedGroup = Groups.FirstOrDefault();
        Raise(nameof(SelectedCount)); Raise(nameof(DeleteLabel));
        ResultsChanged?.Invoke(_all.Count, _all.Where(i => i.Confidence >= Confidence.Medium).Sum(i => i.SizeBytes));
    }

    private async Task CleanAsync()
    {
        var sel = _all.Where(i => i.IsSelected).ToList();
        if (sel.Count == 0) return;
        var dlg = new PreviewWindow(sel) { Owner = Application.Current.MainWindow };
        if (dlg.ShowDialog() != true) { Logger.Op("Cleanup cancelled at preview"); return; }

        IsBusy = true; Status = "Deleting…";
        try
        {
            var results = await Task.Run(() => _cleanup.Execute(sel, CancellationToken.None));
            var done = results.Where(r => r.Success).Select(r => r.Item).ToHashSet();
            var failed = results.Where(r => !r.Success).ToList();
            _all = _all.Where(i => !done.Contains(i)).ToList(); Rebuild();
            Status = $"{done.Count} removed, {failed.Count} failed.";
            var msg = $"{done.Count} item(s) removed.";
            if (failed.Count > 0) msg += $"\n\n{failed.Count} failed:\n" + string.Join("\n", failed.Take(5).Select(f => $"• {f.Item.Path}\n   {f.Message}"));
            MessageBox.Show(msg, "NeoUninstall");
        }
        catch (Exception ex) { Logger.Error("Cleanup failed", ex); Status = "Cleanup failed — see log"; }
        finally { IsBusy = false; CleanupFinished?.Invoke(); }
    }

    private static void RestartAsAdmin()
    {
        try
        {
            Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas" });
            Application.Current.Shutdown();
        }
        catch (System.ComponentModel.Win32Exception) { /* user declined UAC */ }
    }
}
