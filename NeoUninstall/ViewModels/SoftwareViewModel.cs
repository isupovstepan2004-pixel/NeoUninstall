using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Data;
using NeoUninstall.Infrastructure;
using NeoUninstall.Models;
using NeoUninstall.Services;

namespace NeoUninstall.ViewModels;

public sealed class SoftwareViewModel : PageViewModel
{
    private readonly ObservableCollection<InstalledApp> _apps = new();
    private string _search = ""; private InstalledApp? _selected;

    public override string Title => "Software";
    public override string Glyph => "\uE8F1";
    public ICollectionView View { get; }
    public string Search { get => _search; set { if (Set(ref _search, value)) { View.Refresh(); Raise(nameof(Count)); } } }
    public InstalledApp? Selected { get => _selected; set => Set(ref _selected, value); }
    public string Count => $"{View.Cast<object>().Count()} of {_apps.Count} applications";

    public RelayCommand OpenFolderCommand { get; }
    public RelayCommand UninstallCommand { get; }
    public RelayCommand DeepUninstallCommand { get; }
    public event Action<InstalledApp>? DeepUninstallRequested;

    public SoftwareViewModel()
    {
        View = CollectionViewSource.GetDefaultView(_apps);
        View.Filter = o => o is InstalledApp a && (_search.Length == 0 ||
            a.Name.Contains(_search, StringComparison.OrdinalIgnoreCase) ||
            a.Publisher.Contains(_search, StringComparison.OrdinalIgnoreCase));
        View.SortDescriptions.Add(new SortDescription(nameof(InstalledApp.Name), ListSortDirection.Ascending));

        OpenFolderCommand = new(_ => OpenFolder(), _ => Selected != null && Selected.InstallLocation.Length > 0);
        UninstallCommand = new(_ => Uninstall(), _ => Selected != null && Selected.HasUninstaller);
        DeepUninstallCommand = new(_ => DeepUninstall(), _ => Selected != null && Selected.HasUninstaller);
    }

    public void SetApps(IEnumerable<InstalledApp> apps)
    {
        _apps.Clear(); foreach (var a in apps) _apps.Add(a);
        Raise(nameof(Count));
    }

    private void OpenFolder()
    {
        try
        {
            if (Selected == null) return;
            if (!Directory.Exists(Selected.InstallLocation)) { MessageBox.Show("The install folder no longer exists.", "NeoUninstall"); return; }
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{Selected.InstallLocation}\"") { UseShellExecute = true });
        }
        catch (Exception ex) { Logger.Error("Open folder failed", ex); }
    }

    private void DeepUninstall()
    {
        var a = Selected; if (a == null) return;
        var ok = MessageBox.Show($"Deep uninstall of \"{a.Name}\":\n\n1. The official uninstaller runs first — complete it.\n2. NeoUninstall waits for it to finish, then scans for leftovers of this application.\n3. Nothing is deleted until you review the list and confirm.\n\nContinue?",
            "Deep uninstall", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (ok == MessageBoxResult.Yes) DeepUninstallRequested?.Invoke(a);
    }

    /// Standard uninstall only: launches the publisher's own uninstall command. NeoUninstall deletes nothing itself.
    private void Uninstall()
    {
        var a = Selected; if (a == null) return;
        var ok = MessageBox.Show($"Launch the official uninstaller for \"{a.Name}\"?\n\n{a.UninstallString}",
            "Confirm uninstall", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (ok != MessageBoxResult.Yes) return;
        try
        {
            Logger.Info($"Launching official uninstaller for '{a.Name}': {a.UninstallString}");
            Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{a.UninstallString}\"") { UseShellExecute = false, CreateNoWindow = true });
        }
        catch (Exception ex) { Logger.Error($"Uninstaller launch failed for '{a.Name}'", ex); MessageBox.Show("Could not start the uninstaller:\n" + ex.Message); }
    }
}
