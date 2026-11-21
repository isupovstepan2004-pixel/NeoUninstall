using System.Collections.ObjectModel;
using System.Windows;
using NeoUninstall.Infrastructure;
using NeoUninstall.Models;
using NeoUninstall.Services;

namespace NeoUninstall.ViewModels;

public sealed class BackupsViewModel : PageViewModel
{
    private readonly BackupService _svc = new();
    private BackupEntry? _selected;
    public override string Title => "Backup Center";
    public override string Glyph => "\uE8F7";
    public ObservableCollection<BackupEntry> Entries { get; } = new();
    public ObservableCollection<string> Log { get; } = new();
    public BackupEntry? Selected { get => _selected; set => Set(ref _selected, value); }
    public RelayCommand RefreshCommand { get; }
    public RelayCommand RestoreCommand { get; }

    public BackupsViewModel()
    {
        RefreshCommand = new(_ => Refresh());
        RestoreCommand = new(_ => Restore(), _ => Selected != null);
        Refresh();
    }

    public void Refresh()
    {
        Entries.Clear(); foreach (var e in _svc.Load()) Entries.Add(e);
        Log.Clear();
        try
        {
            if (File.Exists(AppPaths.OpsLog))
                foreach (var l in File.ReadLines(AppPaths.OpsLog).TakeLast(150).Reverse()) Log.Add(l);
        }
        catch (Exception ex) { Logger.Warn("Could not read operations log: " + ex.Message); }
    }

    private void Restore()
    {
        var e = Selected; if (e == null) return;
        var ok = MessageBox.Show($"Restore this registry backup?\n\n{e.KeyPath}\n\nThe key will be re-imported from:\n{e.FilePath}",
            "Restore backup", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (ok != MessageBoxResult.Yes) return;
        var success = _svc.Restore(e, out var msg);
        MessageBox.Show(success ? "Backup restored." : "Restore failed:\n" + msg, "NeoUninstall");
        Refresh();
    }
}
