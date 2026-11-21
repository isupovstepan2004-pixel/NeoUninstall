using System.Collections.ObjectModel;
using NeoUninstall.Infrastructure;
using NeoUninstall.Models;
using NeoUninstall.Services;

namespace NeoUninstall.ViewModels;

public sealed class DashboardViewModel : PageViewModel
{
    private readonly InstalledAppScanner _scanner = new();
    private readonly ScanHistoryService _history = new();
    private readonly List<ScanRecord> _records;
    private CancellationTokenSource? _cts;
    private bool _isScanning; private int _appCount, _brokenCount, _leftoverCount;
    private string _status = "Ready", _reclaimable = "—";

    public override string Title => "Dashboard";
    public override string Glyph => "\uE80F";
    public event Action<List<InstalledApp>>? ScanCompleted;
    /// Second stage of the full scan (leftover detection), wired up by MainViewModel.
    public Func<List<InstalledApp>, CancellationToken, Task>? ExtraStage { get; set; }

    public ObservableCollection<ScanRecord> Recent { get; } = new();
    public bool IsScanning { get => _isScanning; private set => Set(ref _isScanning, value); }
    public int AppCount { get => _appCount; private set => Set(ref _appCount, value); }
    public int BrokenCount { get => _brokenCount; private set => Set(ref _brokenCount, value); }
    public int LeftoverCount { get => _leftoverCount; private set => Set(ref _leftoverCount, value); }
    public string Reclaimable { get => _reclaimable; private set => Set(ref _reclaimable, value); }
    public string Status { get => _status; private set => Set(ref _status, value); }

    public RelayCommand StartScanCommand { get; }
    public RelayCommand CancelScanCommand { get; }

    public DashboardViewModel()
    {
        _records = _history.Load();
        foreach (var r in _records.OrderByDescending(r => r.Timestamp).Take(8)) Recent.Add(r);
        if (_records.Count > 0)
        {
            var l = _records.OrderByDescending(r => r.Timestamp).First();
            AppCount = l.AppCount; BrokenCount = l.BrokenCount; LeftoverCount = l.LeftoverCount;
        }
        StartScanCommand = new(async _ => await RunScanAsync(), _ => !IsScanning);
        CancelScanCommand = new(_ => _cts?.Cancel(), _ => IsScanning);
    }

    private int _stT, _stF, _taT, _taF, _seT, _seF;
    public int StartupTotal { get => _stT; private set => Set(ref _stT, value); }
    public int StartupFlagged { get => _stF; private set => Set(ref _stF, value); }
    public int TasksTotal { get => _taT; private set => Set(ref _taT, value); }
    public int TasksFlagged { get => _taF; private set => Set(ref _taF, value); }
    public int ServicesTotal { get => _seT; private set => Set(ref _seT, value); }
    public int ServicesFlagged { get => _seF; private set => Set(ref _seF, value); }
    public void SetAuditSummary(string kind, int total, int flagged)
    {
        switch (kind)
        {
            case "startup": StartupTotal = total; StartupFlagged = flagged; break;
            case "tasks": TasksTotal = total; TasksFlagged = flagged; break;
            case "services": ServicesTotal = total; ServicesFlagged = flagged; break;
        }
    }

    public void SetStatus(string s) => Status = s;
    public void SetLeftoverSummary(int count, long bytes)
    {
        LeftoverCount = count; Reclaimable = InstalledApp.FormatSize(bytes);
    }

    public async Task RunScanAsync()
    {
        if (IsScanning) return;
        _cts = new CancellationTokenSource(); IsScanning = true; Status = "Scanning installed applications…";
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var token = _cts.Token;
            var apps = await Task.Run(() => _scanner.Scan(token), token);
            AppCount = apps.Count; BrokenCount = apps.Count(a => a.IsBroken);
            ScanCompleted?.Invoke(apps);
            if (ExtraStage != null) { Status = "Looking for leftovers…"; await ExtraStage(apps, token); }
            token.ThrowIfCancellationRequested();
            var rec = new ScanRecord { Timestamp = DateTime.Now, Kind = "Full scan", AppCount = AppCount, BrokenCount = BrokenCount, LeftoverCount = LeftoverCount, DurationMs = sw.ElapsedMilliseconds };
            _records.Add(rec); _history.Save(_records); Recent.Insert(0, rec);
            while (Recent.Count > 8) Recent.RemoveAt(Recent.Count - 1);
            Status = "Scan complete";
        }
        catch (OperationCanceledException) { Status = "Scan cancelled"; Logger.Info("Scan cancelled by user"); }
        catch (Exception ex) { Status = "Scan failed — see log"; Logger.Error("Scan failed", ex); }
        finally { IsScanning = false; }
    }
}
