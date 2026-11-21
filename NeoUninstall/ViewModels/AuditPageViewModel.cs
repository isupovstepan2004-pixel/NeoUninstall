using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using NeoUninstall.Infrastructure;
using NeoUninstall.Models;
using NeoUninstall.Services;

namespace NeoUninstall.ViewModels;

public abstract class AuditPageViewModel<T> : PageViewModel where T : class
{
    private readonly ObservableCollection<T> _items = new();
    private List<InstalledApp> _apps = new();
    private string _search = "", _status = "Not scanned yet."; private bool _busy, _showSystem; private T? _selected;

    public ICollectionView View { get; }
    public RelayCommand RefreshCommand { get; }
    public event Action<int, int>? Summary;

    public virtual bool HasSystemFilter => true;
    public abstract string ShowSystemLabel { get; }
    protected abstract List<T> Collect(AppAssociator assoc, CancellationToken ct);
    protected abstract bool IsSystem(T item);
    protected abstract bool IsFlagged(T item);
    protected abstract string SearchText(T item);
    protected virtual void OnSelected(T? item) { }

    public string Search { get => _search; set { if (Set(ref _search, value)) { View.Refresh(); Raise(nameof(Count)); } } }
    public bool ShowSystem { get => _showSystem; set { if (Set(ref _showSystem, value)) { View.Refresh(); Raise(nameof(Count)); } } }
    public bool IsBusy { get => _busy; private set => Set(ref _busy, value); }
    public string Status { get => _status; protected set => Set(ref _status, value); }
    public T? Selected { get => _selected; set { if (Set(ref _selected, value)) OnSelected(value); } }
    public string Count => $"{View.Cast<object>().Count()} of {_items.Count}";

    protected AuditPageViewModel()
    {
        View = CollectionViewSource.GetDefaultView(_items);
        View.Filter = o => o is T t && (_showSystem || !IsSystem(t)) &&
            (_search.Length == 0 || SearchText(t).Contains(_search, StringComparison.OrdinalIgnoreCase));
        RefreshCommand = new(async _ => await LoadAsync(), _ => !IsBusy);
    }

    public async Task LoadAsync(List<InstalledApp>? apps = null, CancellationToken ct = default)
    {
        if (IsBusy) return;
        IsBusy = true; Status = "Scanning…";
        try
        {
            if (apps != null) _apps = apps;
            var known = _apps;
            var list = await Task.Run(() =>
            {
                var a = known.Count > 0 ? known : new InstalledAppScanner().Scan(ct);
                return Collect(new AppAssociator(a), ct);
            }, ct);
            _items.Clear(); foreach (var i in list) _items.Add(i);
            Raise(nameof(Count));
            var visible = list.Where(i => !IsSystem(i)).ToList();
            Status = $"{list.Count} found, {visible.Count} third-party, {visible.Count(IsFlagged)} flagged.";
            Summary?.Invoke(visible.Count, visible.Count(IsFlagged));
        }
        catch (OperationCanceledException) { Status = "Scan cancelled"; }
        catch (Exception ex) { Status = "Scan failed — see log"; Logger.Error($"{Title} scan failed", ex); }
        finally { IsBusy = false; }
    }
}
