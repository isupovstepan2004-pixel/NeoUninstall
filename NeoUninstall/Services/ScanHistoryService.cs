using System.Text.Json;
using NeoUninstall.Models;

namespace NeoUninstall.Services;

public sealed class ScanHistoryService
{
    private static readonly JsonSerializerOptions Opts = new() { WriteIndented = true };
    public List<ScanRecord> Load()
    {
        try
        {
            if (!File.Exists(AppPaths.History)) return new();
            return JsonSerializer.Deserialize<List<ScanRecord>>(File.ReadAllText(AppPaths.History)) ?? new();
        }
        catch (Exception ex) { Logger.Error("Failed to load scan history", ex); return new(); }
    }
    public void Save(List<ScanRecord> items)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.Root);
            File.WriteAllText(AppPaths.History, JsonSerializer.Serialize(items.TakeLast(100).ToList(), Opts));
        }
        catch (Exception ex) { Logger.Error("Failed to save scan history", ex); }
    }
}
