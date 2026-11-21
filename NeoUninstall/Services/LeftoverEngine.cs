using System.Text.RegularExpressions;
using Microsoft.Win32;
using NeoUninstall.Models;

namespace NeoUninstall.Services;

/// Evidence-based leftover detection. Unfamiliar != orphaned: every item carries the reason it was reported.
public sealed class LeftoverEngine
{
    private static readonly Regex DriveRx = new(@"^[A-Za-z]:\\", RegexOptions.Compiled);
    private static readonly (RegistryHive hive, string sub, string prefix)[] RegRoots =
    {
        (RegistryHive.CurrentUser, "SOFTWARE", "HKCU\\SOFTWARE"),
        (RegistryHive.LocalMachine, "SOFTWARE", "HKLM\\SOFTWARE"),
        (RegistryHive.LocalMachine, "SOFTWARE\\WOW6432Node", "HKLM\\SOFTWARE\\WOW6432Node"),
    };

    private readonly HashSet<string> _known = new();
    private readonly List<string> _knownPaths = new();
    private readonly HashSet<string> _seen = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<LeftoverItem> _items = new();
    private readonly Dictionary<string, (string Name, string Reason, bool Targeted)> _removed = new();

    public List<LeftoverItem> Scan(IReadOnlyList<InstalledApp> installed, InstalledApp? target, IProgress<string>? progress, CancellationToken ct)
    {
        _known.Clear(); _knownPaths.Clear(); _seen.Clear(); _items.Clear(); _removed.Clear();

        progress?.Report("Checking uninstall entries…");
        var stale = FindStale(installed, ct);
        BuildKnown(installed, stale);

        progress?.Report("Checking Program Files…");
        OrphanInstallFolders(ct);
        progress?.Report("Checking registry…");
        OrphanRegistryKeys(ct);
        if (target != null) { progress?.Report($"Looking for leftovers of {target.Name}…"); Targeted(target, ct); }
        progress?.Report("Matching application data folders…");
        MatchDataFolders(ct);

        Logger.Info($"Leftover scan finished: {_items.Count} items");
        return _items.ToList();
    }

    // ---------- known software ----------
    private void BuildKnown(IReadOnlyList<InstalledApp> installed, HashSet<InstalledApp> stale)
    {
        foreach (var a in installed)
        {
            if (!stale.Contains(a))
            {
                AddKnown(NameUtil.Norm(a.Name)); AddKnown(NameUtil.Norm(a.Publisher));
                AddKnown(NameUtil.Norm(Path.GetFileName(a.InstallLocation.TrimEnd('\\'))));
            }
            if (a.InstallLocation.Length > 0) _knownPaths.Add(a.InstallLocation.TrimEnd('\\'));
            var exe = InstalledAppScanner.ExtractExe(a.UninstallString);
            var dir = exe == null ? null : SafeDirName(exe);
            if (!string.IsNullOrEmpty(dir)) _knownPaths.Add(dir.TrimEnd('\\'));
        }
    }
    private void AddKnown(string n) { if (n.Length > 0) _known.Add(n); }
    private static string? SafeDirName(string p) { try { return Path.GetDirectoryName(p); } catch { return null; } }

    private bool IsLiveName(string name)
    {
        var n = NameUtil.Norm(name);
        if (n.Length == 0 || _known.Contains(n)) return true;
        if (n.Length >= 4) foreach (var k in _known) if (k.Length >= 4 && (k.StartsWith(n) || n.StartsWith(k))) return true;
        return false;
    }
    private bool IsLiveFolder(string path, string name)
    {
        if (IsLiveName(name)) return true;
        foreach (var p in _knownPaths) if (Under(p, path) || Under(path, p)) return true;
        return false;
    }
    private static bool Under(string child, string parent)
    {
        child = child.TrimEnd('\\') + "\\"; parent = parent.TrimEnd('\\') + "\\";
        return child.StartsWith(parent, StringComparison.OrdinalIgnoreCase);
    }

    // ---------- stale uninstall entries ----------
    private HashSet<InstalledApp> FindStale(IReadOnlyList<InstalledApp> installed, CancellationToken ct)
    {
        var stale = new HashSet<InstalledApp>();
        foreach (var a in installed)
        {
            ct.ThrowIfCancellationRequested();
            if (!a.HasUninstaller) continue;
            var exe = InstalledAppScanner.ExtractExe(a.UninstallString);
            bool exeMissing = exe != null && !Path.GetFileName(exe).Equals("msiexec.exe", StringComparison.OrdinalIgnoreCase)
                              && Path.IsPathRooted(exe) && DriveReady(exe) && !File.Exists(exe);
            if (!exeMissing) continue;
            bool locMissing = a.InstallLocation.Length > 0 && Path.IsPathRooted(a.InstallLocation) && DriveReady(a.InstallLocation) && !Directory.Exists(a.InstallLocation);
            stale.Add(a);
            var reason = locMissing ? "its uninstaller file and install folder no longer exist" : "its registered uninstaller file no longer exists";
            _removed.TryAdd(NameUtil.Norm(a.Name), (a.Name, reason, false));
            AddKey(a.RegistryPath, a.Name, locMissing ? Confidence.High : Confidence.Medium,
                locMissing ? $"Uninstall entry points to a missing uninstaller ({exe}) and a missing install folder ({a.InstallLocation})."
                           : $"Uninstall entry points to an uninstaller that no longer exists: {exe}");
        }
        return stale;
    }
    private static bool DriveReady(string p) { try { var r = Path.GetPathRoot(p); return !string.IsNullOrEmpty(r) && Directory.Exists(r); } catch { return false; } }

    // ---------- install folders ----------
    private void OrphanInstallFolders(CancellationToken ct)
    {
        foreach (var root in ScanRoots.Install.Where(Directory.Exists))
            foreach (var dir in SafeDirs(root))
            {
                ct.ThrowIfCancellationRequested();
                var name = Path.GetFileName(dir);
                if (NameUtil.IsProtected(name) || IsReparse(dir) || IsLiveFolder(dir, name)) continue;
                var (any, uninst, otherExe) = Inspect(dir);
                if (uninst.Count > 0 && otherExe == 0)
                { _removed.TryAdd(NameUtil.Norm(name), (name, "an orphaned install folder", false));
                  AddFolder(dir, name, Confidence.High, $"Contains only an uninstaller binary ({uninst[0]}) as executable code, and no installed program is registered for this folder.", ct); }
                else if (uninst.Count > 0)
                { _removed.TryAdd(NameUtil.Norm(name), (name, "an orphaned install folder", false));
                  AddFolder(dir, name, Confidence.Medium, $"Contains an uninstaller ({uninst[0]}) but also other executables; no installed program is registered for it. It may be a working program whose registration was lost.", ct); }
                else if (!any)
                { _removed.TryAdd(NameUtil.Norm(name), (name, "an empty install folder", false));
                  AddFolder(dir, name, Confidence.Medium, "Folder contains no files and no installed program is registered for it.", ct); }
                else if (otherExe > 0)
                  AddFolder(dir, name, Confidence.Low, "Contains executables but no installed program is registered for it. It may be portable software — inspect before deleting.", ct);
            }
    }

    private static (bool any, List<string> uninst, int otherExe) Inspect(string dir)
    {
        var uninst = new List<string>(); bool any = false; int other = 0;
        var opt = new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 3, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
        try
        {
            foreach (var f in Directory.EnumerateFiles(dir, "*", opt))
            {
                any = true; var n = Path.GetFileName(f);
                if (!n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;
                if (n.StartsWith("unins", StringComparison.OrdinalIgnoreCase) || n.StartsWith("uninstall", StringComparison.OrdinalIgnoreCase)) uninst.Add(n); else other++;
            }
        }
        catch { /* inaccessible */ }
        return (any, uninst, other);
    }

    // ---------- orphaned registry keys ----------
    private void OrphanRegistryKeys(CancellationToken ct)
    {
        foreach (var (hive, sub, prefix) in RegRoots)
        {
            try
            {
                using var bk = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
                using var root = bk.OpenSubKey(sub);
                if (root == null) continue;
                foreach (var name in root.GetSubKeyNames())
                {
                    ct.ThrowIfCancellationRequested();
                    if (NameUtil.IsProtected(name) || IsLiveName(name)) continue;
                    try
                    {
                        using var k = root.OpenSubKey(name);
                        if (k == null) continue;
                        var paths = new List<(string val, string path)>();
                        CollectPaths(k, 0, paths);
                        if (paths.Count == 0) continue; // no evidence either way → never reported
                        if (paths.Any(p => File.Exists(p.path) || Directory.Exists(p.path))) continue;
                        var ex = paths[0];
                        _removed.TryAdd(NameUtil.Norm(name), (name, "its registry key references only missing locations", false));
                        AddKey($"{prefix}\\{name}", name, Confidence.Medium,
                            $"All {paths.Count} path value(s) in this key point to locations that no longer exist (e.g. '{ex.val}' = {ex.path}), and no installed program uses this name.");
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception e) { Logger.Warn($"Registry key {name} skipped: {e.Message}"); }
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception e) { Logger.Error($"Registry root {prefix} failed", e); }
        }
    }

    private static void CollectPaths(RegistryKey k, int depth, List<(string, string)> list)
    {
        foreach (var vn in k.GetValueNames())
        {
            if (list.Count >= 20) return;
            var kind = k.GetValueKind(vn);
            if (kind is RegistryValueKind.String or RegistryValueKind.ExpandString && TryPath(k.GetValue(vn) as string, out var p))
                list.Add((vn.Length == 0 ? "(Default)" : vn, p));
        }
        if (depth >= 3) return;
        foreach (var sn in k.GetSubKeyNames())
        {
            if (list.Count >= 20) return;
            try { using var sk = k.OpenSubKey(sn); if (sk != null) CollectPaths(sk, depth + 1, list); } catch { /* skip */ }
        }
    }

    private static bool TryPath(string? s, out string path)
    {
        path = "";
        if (string.IsNullOrWhiteSpace(s)) return false;
        s = Environment.ExpandEnvironmentVariables(s.Trim()).Trim('"');
        if (!DriveRx.IsMatch(s) || s.Contains(';')) return false;
        var i = s.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        if (i > 0) s = s[..(i + 4)];
        s = s.TrimEnd('\\');
        if (s.Length <= 3 || !DriveReady(s)) return false;
        path = s; return true;
    }

    // ---------- targeted (after Deep Uninstall) ----------
    private void Targeted(InstalledApp t, CancellationToken ct)
    {
        var n = NameUtil.Norm(t.Name); var pub = NameUtil.Norm(t.Publisher);
        if (n.Length < 3 || NameUtil.Protected.Contains(n)) return;
        if (_known.Contains(n)) { Logger.Warn($"Targeted scan skipped: another installed entry is also named '{t.Name}'"); return; }
        _removed[n] = (t.Name, "the application you just uninstalled", true);

        var loc = t.InstallLocation.Trim().TrimEnd('\\');
        if (loc.Length > 0 && Directory.Exists(loc))
            AddFolder(loc, t.Name, Confidence.High, $"Registered install location of the removed application '{t.Name}'.", ct);

        foreach (var root in ScanRoots.All.Where(Directory.Exists))
            foreach (var d in SafeDirs(root))
            {
                ct.ThrowIfCancellationRequested();
                var dn = NameUtil.Norm(Path.GetFileName(d));
                if (dn == n) AddFolder(d, t.Name, Confidence.High, $"Folder name matches the removed application '{t.Name}'.", ct);
                else if (pub.Length >= 3 && dn == pub && !NameUtil.Protected.Contains(dn) && !IsReparse(d))
                    foreach (var sd in SafeDirs(d))
                        if (NameUtil.Norm(Path.GetFileName(sd)) == n)
                            AddFolder(sd, t.Name, Confidence.High, $"Inside publisher folder '{Path.GetFileName(d)}' and named after the removed application '{t.Name}'.", ct);
            }

        foreach (var (hive, sub, prefix) in RegRoots)
        {
            try
            {
                using var bk = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
                using var root = bk.OpenSubKey(sub);
                if (root == null) continue;
                foreach (var name in root.GetSubKeyNames())
                {
                    var nn = NameUtil.Norm(name);
                    if (nn == n) AddKey($"{prefix}\\{name}", t.Name, Confidence.High, $"Registry key name matches the removed application '{t.Name}'.");
                    else if (pub.Length >= 3 && nn == pub && !NameUtil.Protected.Contains(nn))
                    {
                        using var pk = root.OpenSubKey(name);
                        if (pk == null) continue;
                        foreach (var s in pk.GetSubKeyNames())
                            if (NameUtil.Norm(s) == n)
                                AddKey($"{prefix}\\{name}\\{s}", t.Name, Confidence.High, $"Under publisher key '{name}' and named after the removed application '{t.Name}'.");
                    }
                }
            }
            catch (Exception e) { Logger.Warn($"Targeted registry scan failed for {prefix}: {e.Message}"); }
        }
    }

    // ---------- application data folders ----------
    private void MatchDataFolders(CancellationToken ct)
    {
        foreach (var root in ScanRoots.Data.Where(Directory.Exists))
            foreach (var d in SafeDirs(root))
            {
                ct.ThrowIfCancellationRequested();
                var name = Path.GetFileName(d);
                if (NameUtil.IsProtected(name) || IsReparse(d)) continue;
                if (!_removed.TryGetValue(NameUtil.Norm(name), out var r)) continue;
                if (IsLiveName(name)) continue;
                AddFolder(d, r.Name, r.Targeted ? Confidence.High : Confidence.Medium,
                    $"Folder name matches '{r.Name}', which was identified as removed ({r.Reason}).", ct);
            }
    }

    // ---------- helpers ----------
    private void AddFolder(string path, string app, Confidence c, string evidence, CancellationToken ct)
    {
        path = path.TrimEnd('\\');
        if (!_seen.Add(path) || !SafetyGuard.CheckFolder(path, out _)) return;
        var (bytes, files) = DirSize(path, ct);
        _items.Add(new LeftoverItem { Kind = LeftoverKind.Folder, Path = path, SizeBytes = bytes, FileCount = files, Confidence = c, Evidence = evidence, AppName = app });
    }
    private void AddKey(string path, string app, Confidence c, string evidence)
    {
        if (!_seen.Add(path) || !SafetyGuard.CheckRegistry(path, out _)) return;
        _items.Add(new LeftoverItem { Kind = LeftoverKind.RegistryKey, Path = path, Confidence = c, Evidence = evidence, AppName = app });
    }
    private static List<string> SafeDirs(string root) { try { return Directory.EnumerateDirectories(root).ToList(); } catch { return new(); } }
    private static bool IsReparse(string d) { try { return new DirectoryInfo(d).Attributes.HasFlag(FileAttributes.ReparsePoint); } catch { return true; } }
    private static (long, int) DirSize(string dir, CancellationToken ct)
    {
        long b = 0; int n = 0;
        var opt = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
        try
        {
            foreach (var f in new DirectoryInfo(dir).EnumerateFiles("*", opt))
            { ct.ThrowIfCancellationRequested(); try { b += f.Length; n++; } catch { } }
        }
        catch (OperationCanceledException) { throw; }
        catch { }
        return (b, n);
    }
}
