using System.Text;
using Microsoft.Win32;

namespace NeoUninstall.Services;

public static class NameUtil
{
    private static readonly System.Text.RegularExpressions.Regex Paren = new(@"[\(\[][^\)\]]*[\)\]]", System.Text.RegularExpressions.RegexOptions.Compiled);

    public static string Norm(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        s = Paren.Replace(s, " ");
        var sb = new StringBuilder();
        foreach (var c in s) if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
        return sb.ToString();
    }

    /// Normalized names that are never reported or deleted: Windows components, shared libraries, drivers, vendor trees.
    public static readonly HashSet<string> Protected = new()
    {
        "microsoft","windows","windowsapps","windowsnt","windowsdefender","windowsmail","windowsmediaplayer","windowspowershell",
        "windowssidebar","windowsphotoviewer","windowskits","commonfiles","internetexplorer","microsoftedge","microsoftedgecore",
        "modifiablewindowsapps","referenceassemblies","dotnet","msbuild","packages","packagecache","uninstallinformation","temp","tmp",
        "system32","wow6432node","classes","policies","clients","registeredapplications","odbc","intel","intelcorporation","nvidia",
        "nvidiacorporation","amd","advancedmicrodevices","realtek","dell","hp","lenovo","asus","apple","comms","connecteddevicesplatform",
        "publishers","programs","desktop","default","public","allusers","microsoftcorporation","visualstudio","microsoftvisualstudio",
    };
    public static bool IsProtected(string name) => Protected.Contains(Norm(name));
}

public static class ScanRoots
{
    private static string[] Distinct(params string[] p) => p.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    public static readonly string[] Install = Distinct(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));
    public static readonly string[] Data = Distinct(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "LocalLow"));
    public static readonly string[] All = Install.Concat(Data).ToArray();
}

public static class RegistryPaths
{
    public static (RegistryHive hive, string sub) Parse(string path)
    {
        var i = path.IndexOf('\\');
        if (i < 0) throw new ArgumentException("Invalid registry path: " + path);
        var hive = path[..i].ToUpperInvariant() switch
        {
            "HKCU" => RegistryHive.CurrentUser,
            "HKLM" => RegistryHive.LocalMachine,
            _ => throw new ArgumentException("Unsupported hive: " + path)
        };
        return (hive, path[(i + 1)..]);
    }
}

/// Last line of defence: runs again at deletion time, independent of what the scanner reported.
public static class SafetyGuard
{
    private static string Trim(string p) => p.TrimEnd('\\', '/');
    private static readonly string[] VendorRoots = { @"HKCU\SOFTWARE", @"HKLM\SOFTWARE", @"HKLM\SOFTWARE\WOW6432Node" };
    private const string UninstSuffix = @"\Microsoft\Windows\CurrentVersion\Uninstall";

    public static bool CheckFolder(string path, out string reason)
    {
        reason = "";
        string full;
        try { full = Trim(Path.GetFullPath(path)); } catch { reason = "Invalid path"; return false; }
        var parent = Path.GetDirectoryName(full); var name = Path.GetFileName(full);
        if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(name)) { reason = "Drive roots are never deleted"; return false; }
        bool IsRoot(string p) => ScanRoots.All.Any(r => string.Equals(Trim(r), Trim(p), StringComparison.OrdinalIgnoreCase));
        bool ok = IsRoot(parent) ||
                  (IsRoot(Path.GetDirectoryName(parent) ?? "") && !NameUtil.IsProtected(Path.GetFileName(parent)));
        if (!ok) { reason = "Outside the allowed application folders"; return false; }
        if (NameUtil.IsProtected(name)) { reason = "Protected system, driver or shared folder"; return false; }
        try
        {
            if (Directory.Exists(full) && new DirectoryInfo(full).Attributes.HasFlag(FileAttributes.ReparsePoint))
            { reason = "Symbolic link or junction"; return false; }
        }
        catch { /* ignore */ }
        return true;
    }

    public static bool CheckRegistry(string path, out string reason)
    {
        reason = "";
        path = path.TrimEnd('\\');
        var i = path.LastIndexOf('\\');
        if (i < 0) { reason = "Invalid registry path"; return false; }
        var parent = path[..i]; var name = path[(i + 1)..];
        var cmp = StringComparer.OrdinalIgnoreCase;
        if (VendorRoots.Any(r => cmp.Equals(r + UninstSuffix, parent))) return true; // uninstall entries
        if (VendorRoots.Contains(parent, cmp))
        {
            if (NameUtil.IsProtected(name)) { reason = "Protected registry key"; return false; }
            return true;
        }
        var j = parent.LastIndexOf('\\');
        if (j > 0 && VendorRoots.Contains(parent[..j], cmp) && !NameUtil.IsProtected(parent[(j + 1)..]) && !NameUtil.IsProtected(name)) return true;
        reason = "Outside the allowed registry locations"; return false;
    }
}
