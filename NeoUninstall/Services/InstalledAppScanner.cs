using System.Globalization;
using Microsoft.Win32;
using NeoUninstall.Models;

namespace NeoUninstall.Services;

/// Reads Windows uninstall registry entries (64-bit, 32-bit/WOW6432Node, per-user). Read-only.
public sealed class InstalledAppScanner
{
    private const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

    public List<InstalledApp> Scan(CancellationToken ct)
    {
        var result = new List<InstalledApp>();
        var sources = new (RegistryHive hive, RegistryView view, string arch, string label)[]
        {
            (RegistryHive.LocalMachine, RegistryView.Registry64, "64-bit", "HKLM\\SOFTWARE"),
            (RegistryHive.LocalMachine, RegistryView.Registry32, "32-bit", "HKLM\\SOFTWARE\\WOW6432Node"),
            (RegistryHive.CurrentUser,  RegistryView.Registry64, "User",   "HKCU\\SOFTWARE"),
        };
        foreach (var s in sources)
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(s.hive, s.view);
                using var root = baseKey.OpenSubKey(UninstallKey);
                if (root == null) continue;
                foreach (var name in root.GetSubKeyNames())
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        using var k = root.OpenSubKey(name);
                        var app = k == null ? null : Read(k, s.arch, $"{s.label}\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\{name}");
                        if (app != null) result.Add(app);
                    }
                    catch (Exception ex) { Logger.Warn($"Skipped uninstall key {name}: {ex.Message}"); }
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { Logger.Error($"Failed reading {s.label}", ex); }
        }
        MarkDuplicates(result);
        Logger.Info($"Installed-app scan finished: {result.Count} entries");
        return result;
    }

    private static InstalledApp? Read(RegistryKey k, string arch, string path)
    {
        var name = (k.GetValue("DisplayName") as string)?.Trim();
        if (string.IsNullOrEmpty(name)) return null;
        if (k.GetValue("SystemComponent") is int sc && sc == 1) return null;
        if (!string.IsNullOrEmpty(k.GetValue("ParentKeyName") as string)) return null; // updates/patches
        if (k.GetValue("ReleaseType") is string rt && (rt is "Update" or "Hotfix" or "Security Update")) return null;

        long size = k.GetValue("EstimatedSize") is int kb && kb > 0 ? kb * 1024L : 0;
        DateTime? date = null;
        if (k.GetValue("InstallDate") is string ds &&
            DateTime.TryParseExact(ds, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) date = d;

        var app = new InstalledApp
        {
            Name = name,
            Publisher = (k.GetValue("Publisher") as string)?.Trim() ?? "",
            Version = (k.GetValue("DisplayVersion") as string)?.Trim() ?? "",
            InstallDate = date,
            InstallLocation = (k.GetValue("InstallLocation") as string)?.Trim().Trim('"') ?? "",
            UninstallString = (k.GetValue("UninstallString") as string)?.Trim() ?? "",
            QuietUninstallString = (k.GetValue("QuietUninstallString") as string)?.Trim() ?? "",
            SizeBytes = size, Architecture = arch, RegistryPath = path,
        };
        CheckBroken(app);
        return app;
    }

    private static void CheckBroken(InstalledApp a)
    {
        if (!a.HasUninstaller) { a.IsBroken = true; a.BrokenReason = "No uninstall command registered."; return; }
        var exe = ExtractExe(a.UninstallString);
        if (exe != null && !Path.GetFileName(exe).Equals("msiexec.exe", StringComparison.OrdinalIgnoreCase)
            && Path.IsPathRooted(exe) && !File.Exists(exe))
        { a.IsBroken = true; a.BrokenReason = $"Uninstaller not found: {exe}"; return; }
        if (a.InstallLocation.Length > 0 && Path.IsPathRooted(a.InstallLocation) && !Directory.Exists(a.InstallLocation))
        { a.IsBroken = true; a.BrokenReason = $"Install location missing: {a.InstallLocation}"; }
    }

    internal static string? ExtractExe(string cmd)
    {
        cmd = Environment.ExpandEnvironmentVariables(cmd.Trim());
        if (cmd.Length == 0) return null;
        if (cmd[0] == '"') { var e = cmd.IndexOf('"', 1); return e > 1 ? cmd[1..e] : null; }
        var i = cmd.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        return i > 0 ? cmd[..(i + 4)] : null;
    }

    private static void MarkDuplicates(List<InstalledApp> apps)
    {
        foreach (var g in apps.GroupBy(a => (a.Name.ToLowerInvariant(), a.Version)).Where(g => g.Count() > 1))
            foreach (var a in g) a.IsDuplicate = true;
    }
}
