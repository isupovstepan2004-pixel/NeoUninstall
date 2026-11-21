using NeoUninstall.Models;

namespace NeoUninstall.Services;

public sealed record Association(string AppName, Confidence Confidence, string Evidence);

public static class PathUtil
{
    public static bool Under(string child, string parent)
    {
        child = child.TrimEnd('\\') + "\\"; parent = parent.TrimEnd('\\') + "\\";
        return child.StartsWith(parent, StringComparison.OrdinalIgnoreCase);
    }
    public static bool DriveReady(string p) { try { var r = Path.GetPathRoot(p); return !string.IsNullOrEmpty(r) && Directory.Exists(r); } catch { return false; } }
    public static string Publisher(string exe)
    {
        try { return File.Exists(exe) ? System.Diagnostics.FileVersionInfo.GetVersionInfo(exe).CompanyName?.Trim() ?? "" : ""; }
        catch { return ""; }
    }
}

/// Links an executable to an installed application — only with stated evidence.
public sealed class AppAssociator
{
    private readonly List<(InstalledApp app, List<string> roots)> _apps = new();
    private static readonly string WinDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

    public AppAssociator(IEnumerable<InstalledApp> apps)
    {
        foreach (var a in apps)
        {
            var roots = new List<string>();
            void Add(string? r)
            {
                if (string.IsNullOrWhiteSpace(r) || !Path.IsPathRooted(r)) return;
                r = r.TrimEnd('\\');
                if (Path.GetDirectoryName(r) == null || PathUtil.Under(r, WinDir)) return;           // drive roots, Windows dir
                if (ScanRoots.All.Any(x => string.Equals(x.TrimEnd('\\'), r, StringComparison.OrdinalIgnoreCase))) return; // shared roots
                roots.Add(r);
            }
            Add(a.InstallLocation);
            var exe = InstalledAppScanner.ExtractExe(a.UninstallString);
            if (exe != null && !Path.GetFileName(exe).Equals("msiexec.exe", StringComparison.OrdinalIgnoreCase)) { try { Add(Path.GetDirectoryName(exe)); } catch { } }
            _apps.Add((a, roots));
        }
    }

    public Association? Find(string? exePath, string publisher)
    {
        if (!string.IsNullOrEmpty(exePath) && Path.IsPathRooted(exePath))
            foreach (var (app, roots) in _apps)
                foreach (var r in roots)
                    if (PathUtil.Under(exePath, r))
                        return new Association(app.Name, Confidence.High, $"Executable is inside the install folder of '{app.Name}' ({r}).");
        var pn = NameUtil.Norm(publisher);
        if (pn.Length >= 3)
        {
            var m = _apps.FirstOrDefault(x => NameUtil.Norm(x.app.Publisher) == pn);
            if (m.app != null) return new Association(m.app.Name, Confidence.Medium, $"Publisher '{publisher}' matches the publisher of installed application '{m.app.Name}'.");
        }
        return null;
    }
}
