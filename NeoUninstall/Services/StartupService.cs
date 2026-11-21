using System.Diagnostics;
using Microsoft.Win32;
using NeoUninstall.Models;

namespace NeoUninstall.Services;

/// Reads Run keys and Startup folders. Enable/disable uses the same StartupApproved flags as Task Manager —
/// nothing is deleted, and the original entry stays intact.
public static class StartupService
{
    private const string ApprovedBase = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\";
    private const string RunPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";

    public static List<StartupEntry> Scan(AppAssociator assoc, CancellationToken ct)
    {
        var list = new List<StartupEntry>();
        var runs = new (RegistryHive hive, string path, string label, string approved, string prefix)[]
        {
            (RegistryHive.CurrentUser,  RunPath, "HKCU Run", "Run", "HKCU"),
            (RegistryHive.LocalMachine, RunPath, "HKLM Run", "Run", "HKLM"),
            (RegistryHive.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run", "HKLM Run (32-bit)", "Run32", "HKLM"),
        };
        foreach (var r in runs)
        {
            try
            {
                using var bk = RegistryKey.OpenBaseKey(r.hive, RegistryView.Registry64);
                using var k = bk.OpenSubKey(r.path);
                if (k == null) continue;
                foreach (var vn in k.GetValueNames())
                {
                    ct.ThrowIfCancellationRequested();
                    if (k.GetValue(vn) is not string cmd || string.IsNullOrWhiteSpace(cmd)) continue;
                    list.Add(Build(vn.Length == 0 ? "(Default)" : vn, cmd, InstalledAppScanner.ExtractExe(cmd), r.label, $"{r.prefix}\\{r.path}", r.hive, r.approved, vn, assoc));
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { Logger.Error($"Startup scan failed for {r.label}", ex); }
        }

        var folders = new (string dir, RegistryHive hive, string label)[]
        {
            (Environment.GetFolderPath(Environment.SpecialFolder.Startup), RegistryHive.CurrentUser, "User Startup folder"),
            (Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), RegistryHive.LocalMachine, "All-users Startup folder"),
        };
        foreach (var f in folders)
        {
            try
            {
                if (string.IsNullOrEmpty(f.dir) || !Directory.Exists(f.dir)) continue;
                foreach (var file in Directory.EnumerateFiles(f.dir))
                {
                    ct.ThrowIfCancellationRequested();
                    var fn = Path.GetFileName(file);
                    if (fn.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
                    var target = file.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) ? ResolveShortcut(file) : file;
                    list.Add(Build(Path.GetFileNameWithoutExtension(fn), file, target, f.label, f.dir, f.hive, "StartupFolder", fn, assoc));
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { Logger.Error($"Startup folder scan failed: {f.dir}", ex); }
        }
        Logger.Info($"Startup scan finished: {list.Count} entries");
        return list.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static StartupEntry Build(string name, string command, string? exe, string source, string location,
        RegistryHive hive, string approvedSub, string valueName, AppAssociator assoc)
    {
        exe = Environment.ExpandEnvironmentVariables(exe ?? "");
        bool rooted = exe.Length > 0 && Path.IsPathRooted(exe);
        bool missing = rooted && PathUtil.DriveReady(exe) && !File.Exists(exe);
        var pub = rooted && !missing ? PathUtil.Publisher(exe) : "";
        var a = assoc.Find(exe, pub);
        var evidence = missing ? $"Target file not found: {exe}. " : "";
        evidence += a != null ? a.Evidence : "No installed application could be linked to this entry.";
        return new StartupEntry
        {
            Name = name, Command = command, ExePath = exe, Publisher = pub, Source = source, Location = location,
            ApprovedHive = hive, ApprovedSub = approvedSub, ApprovedValueName = valueName, TargetMissing = missing,
            Association = a?.AppName ?? "", Evidence = evidence, IsEnabled = ReadEnabled(hive, approvedSub, valueName),
        };
    }

    public static bool ReadEnabled(RegistryHive hive, string sub, string valueName)
    {
        try
        {
            using var bk = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            using var k = bk.OpenSubKey(ApprovedBase + sub);
            if (k?.GetValue(valueName) is byte[] b && b.Length > 0) return (b[0] & 1) == 0;
        }
        catch { /* treat as enabled */ }
        return true;
    }

    public static void SetEnabled(StartupEntry e, bool enable)
    {
        using var bk = RegistryKey.OpenBaseKey(e.ApprovedHive, RegistryView.Registry64);
        using var k = bk.CreateSubKey(ApprovedBase + e.ApprovedSub, true);
        var cur = k.GetValue(e.ApprovedValueName) as byte[];
        var data = cur is { Length: >= 12 } ? (byte[])cur.Clone() : new byte[12];
        if (enable) { data[0] = 2; Array.Clear(data, 4, 8); }
        else { data[0] = 3; BitConverter.GetBytes(DateTime.UtcNow.ToFileTimeUtc()).CopyTo(data, 4); }
        k.SetValue(e.ApprovedValueName, data, RegistryValueKind.Binary);
    }

    private static string ResolveShortcut(string lnk)
    {
        try
        {
            var t = Type.GetTypeFromProgID("WScript.Shell"); if (t == null) return "";
            dynamic sh = Activator.CreateInstance(t)!;
            dynamic sc = sh.CreateShortcut(lnk);
            string target = sc.TargetPath;
            return target ?? "";
        }
        catch { return ""; }
    }
}
