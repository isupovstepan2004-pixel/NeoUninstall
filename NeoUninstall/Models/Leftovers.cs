using NeoUninstall.Infrastructure;

namespace NeoUninstall.Models;

public enum Confidence { Low = 0, Medium = 1, High = 2 }
public enum LeftoverKind { Folder, RegistryKey }

public sealed class LeftoverItem : ObservableObject
{
    private bool _selected;
    public LeftoverKind Kind { get; init; }
    public string Path { get; init; } = "";
    public long SizeBytes { get; init; }
    public int FileCount { get; init; }
    public Confidence Confidence { get; init; }
    public string Evidence { get; init; } = "";
    public string AppName { get; init; } = "";
    public bool IsSelected { get => _selected; set => Set(ref _selected, value); }

    public string KindText => Kind == LeftoverKind.Folder ? "Folder" : "Registry";
    public string ConfidenceText => Confidence.ToString();
    public string SizeText => Kind == LeftoverKind.Folder ? InstalledApp.FormatSize(SizeBytes) : "—";
}

public sealed class AppGroup
{
    public string Name { get; init; } = "";
    public List<LeftoverItem> Items { get; init; } = new();
    public int FolderCount => Items.Count(i => i.Kind == LeftoverKind.Folder);
    public int FileCount => Items.Where(i => i.Kind == LeftoverKind.Folder).Sum(i => i.FileCount);
    public int RegistryCount => Items.Count(i => i.Kind == LeftoverKind.RegistryKey);
    public long TotalSize => Items.Sum(i => i.SizeBytes);
    public string TopConfidence => Items.Max(i => i.Confidence).ToString() + " confidence";
    public string Summary => $"{FolderCount} folders · {FileCount} files · {RegistryCount} registry keys · {InstalledApp.FormatSize(TotalSize)}";

    public static List<AppGroup> Build(IEnumerable<LeftoverItem> items) =>
        items.GroupBy(i => Services.NameUtil.Norm(i.AppName))
             .Select(g => new AppGroup { Name = g.First().AppName, Items = g.OrderByDescending(i => i.Confidence).ThenByDescending(i => i.SizeBytes).ToList() })
             .OrderByDescending(g => g.Items.Max(i => i.Confidence)).ThenByDescending(g => g.TotalSize).ToList();
}

public sealed class BackupEntry
{
    public string Id { get; set; } = "";
    public DateTime Timestamp { get; set; }
    public string KeyPath { get; set; } = "";
    public string FilePath { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public DateTime? RestoredAt { get; set; }
    [System.Text.Json.Serialization.JsonIgnore] public string Integrity { get; set; } = "Not checked";
    [System.Text.Json.Serialization.JsonIgnore] public string StatusText => RestoredAt != null ? $"Restored {RestoredAt:yyyy-MM-dd HH:mm}" : Integrity;
    [System.Text.Json.Serialization.JsonIgnore] public string TimeText => Timestamp.ToString("yyyy-MM-dd HH:mm:ss");
}
