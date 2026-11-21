using System.ServiceProcess;
using Microsoft.Win32;
using NeoUninstall.Models;

namespace NeoUninstall.Services;

/// Read-only. NeoUninstall never stops, disables or removes services.
public static class ServiceScanner
{
    private static readonly string WinDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

    public static List<ServiceEntry> Scan(AppAssociator assoc, CancellationToken ct)
    {
        var status = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try { foreach (var s in ServiceController.GetServices()) { status[s.ServiceName] = s.Status.ToString(); s.Dispose(); } }
        catch (Exception ex) { Logger.Warn("Service status query failed: " + ex.Message); }

        var list = new List<ServiceEntry>();
        try
        {
            using var root = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services");
            if (root == null) return list;
            foreach (var name in root.GetSubKeyNames())
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    using var k = root.OpenSubKey(name);
                    if (k == null || k.GetValue("Type") is not int type || (type & 0x30) == 0) continue; // drivers are excluded
                    var image = k.GetValue("ImagePath") as string ?? "";
                    var exe = Environment.ExpandEnvironmentVariables(InstalledAppScanner.ExtractExe(image) ?? "");
                    if (Path.GetFileName(exe).Equals("svchost.exe", StringComparison.OrdinalIgnoreCase))
                    {
                        using var p = k.OpenSubKey("Parameters");
                        if (p?.GetValue("ServiceDll") is string dll) exe = Environment.ExpandEnvironmentVariables(dll).Trim('"');
                    }
                    list.Add(Build(name, k, exe, status, assoc));
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { Logger.Warn($"Service {name} skipped: {ex.Message}"); }
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { Logger.Error("Service scan failed", ex); }
        Logger.Info($"Service scan finished: {list.Count} services");
        return list.OrderBy(s => s.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static ServiceEntry Build(string name, RegistryKey k, string exe, Dictionary<string, string> status, AppAssociator assoc)
    {
        var display = k.GetValue("DisplayName") as string;
        if (string.IsNullOrWhiteSpace(display) || display.StartsWith('@')) display = name;
        bool rooted = exe.Length > 0 && Path.IsPathRooted(exe);
        bool missing = rooted && PathUtil.DriveReady(exe) && !File.Exists(exe);
        var pub = rooted && !missing ? PathUtil.Publisher(exe) : "";
        bool system = (rooted && PathUtil.Under(exe, WinDir)) || pub.Contains("Microsoft", StringComparison.OrdinalIgnoreCase);
        var a = assoc.Find(exe, pub);

        string evidence; bool flagged = false;
        if (missing)
        {
            evidence = $"Service executable not found: {exe}.";
            if (!system && a == null) { flagged = true; evidence += " No installed application could be linked to this service."; }
            else if (a != null) evidence += " " + a.Evidence;
        }
        else evidence = system ? "Windows / Microsoft component." : a?.Evidence ?? "No installed application could be linked to this service.";

        var start = k.GetValue("Start") is int sv ? sv : -1;
        var mode = start switch { 0 => "Boot", 1 => "System", 2 => (k.GetValue("DelayedAutostart") is int ds && ds == 1 ? "Automatic (delayed)" : "Automatic"), 3 => "Manual", 4 => "Disabled", _ => "?" };
        return new ServiceEntry
        {
            Name = name, DisplayName = display, Publisher = pub, ExePath = exe, StartMode = mode,
            Status = status.TryGetValue(name, out var st) ? st : "Unknown", IsSystem = system,
            Association = a?.AppName ?? "", Evidence = evidence, Flagged = flagged,
        };
    }
}
