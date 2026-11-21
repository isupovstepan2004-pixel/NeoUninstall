namespace NeoUninstall.Models;

public sealed class InstalledApp
{
    public string Name { get; init; } = "";
    public string Publisher { get; init; } = "";
    public string Version { get; init; } = "";
    public DateTime? InstallDate { get; init; }
    public string InstallLocation { get; init; } = "";
    public string UninstallString { get; init; } = "";
    public string QuietUninstallString { get; init; } = "";
    public long SizeBytes { get; init; }
    public string Architecture { get; init; } = "";
    public string RegistryPath { get; init; } = "";
    public bool IsBroken { get; set; }
    public string BrokenReason { get; set; } = "";
    public bool IsDuplicate { get; set; }

    public bool HasUninstaller => !string.IsNullOrWhiteSpace(UninstallString);
    public string InstallDateText => InstallDate?.ToString("yyyy-MM-dd") ?? "—";
    public string SizeText => SizeBytes > 0 ? FormatSize(SizeBytes) : "—";
    public string Status => IsBroken ? "Broken entry" : IsDuplicate ? "Duplicate entry" : "OK";

    public static string FormatSize(long b)
    {
        string[] u = { "B", "KB", "MB", "GB", "TB" }; double v = b; int i = 0;
        while (v >= 1024 && i < u.Length - 1) { v /= 1024; i++; }
        return $"{v:0.#} {u[i]}";
    }
}

public sealed class ScanRecord
{
    public DateTime Timestamp { get; set; }
    public string Kind { get; set; } = "";
    public int AppCount { get; set; }
    public int BrokenCount { get; set; }
    public int LeftoverCount { get; set; }
    public long DurationMs { get; set; }
    public string Summary => $"{Timestamp:yyyy-MM-dd HH:mm} · {Kind} · {AppCount} apps, {BrokenCount} broken entries, {LeftoverCount} leftover items · {DurationMs / 1000.0:0.0}s";
}
