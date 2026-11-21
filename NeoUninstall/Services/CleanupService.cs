using Microsoft.VisualBasic.FileIO;
using Microsoft.Win32;
using NeoUninstall.Models;

namespace NeoUninstall.Services;

public sealed record OpResult(LeftoverItem Item, bool Success, string Message);

public sealed class CleanupService
{
    private readonly BackupService _backup = new();

    public List<OpResult> Execute(IReadOnlyList<LeftoverItem> items, CancellationToken ct)
    {
        var results = new List<OpResult>();
        Logger.Op($"Cleanup started: {items.Count} item(s) approved by user");
        foreach (var it in items)
        {
            ct.ThrowIfCancellationRequested();
            OpResult r;
            try { r = it.Kind == LeftoverKind.Folder ? DeleteFolder(it) : DeleteKey(it); }
            catch (UnauthorizedAccessException) { r = new(it, false, "Access denied — restart NeoUninstall as administrator."); }
            catch (Exception ex) { r = new(it, false, ex.Message); }
            Logger.Op($"{(r.Success ? "DELETED" : "FAILED")} [{it.KindText}] {it.Path} — {r.Message}");
            results.Add(r);
        }
        return results;
    }

    private static OpResult DeleteFolder(LeftoverItem it)
    {
        if (!SafetyGuard.CheckFolder(it.Path, out var why)) return new(it, false, "Blocked by safety guard: " + why);
        if (!Directory.Exists(it.Path)) return new(it, true, "Already gone");
        FileSystem.DeleteDirectory(it.Path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin, UICancelOption.ThrowException);
        return new(it, true, "Moved to Recycle Bin");
    }

    private OpResult DeleteKey(LeftoverItem it)
    {
        if (!SafetyGuard.CheckRegistry(it.Path, out var why)) return new(it, false, "Blocked by safety guard: " + why);
        var (hive, sub) = RegistryPaths.Parse(it.Path);
        using var bk = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
        using (var probe = bk.OpenSubKey(sub)) if (probe == null) return new(it, true, "Already gone");
        var backup = _backup.BackupKey(it.Path);
        if (backup == null) return new(it, false, "Backup could not be created or verified — key NOT deleted.");
        bk.DeleteSubKeyTree(sub, false);
        return new(it, true, $"Deleted; backup {Path.GetFileName(backup.FilePath)}");
    }
}

public static class UninstallerRunner
{
    /// Runs the application's own uninstall command and waits for the launched process to exit.
    public static int RunAndWait(string uninstallString)
    {
        try
        {
            using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c \"{uninstallString}\"") { UseShellExecute = false, CreateNoWindow = true });
            p!.WaitForExit();
            return p.ExitCode;
        }
        catch (Exception ex) { Logger.Error("Uninstaller run failed", ex); return -1; }
    }
}
