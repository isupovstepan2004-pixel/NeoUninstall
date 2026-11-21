using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using NeoUninstall.Models;

namespace NeoUninstall.Services;

/// Registry backups via reg.exe export; every backup is verified (header + SHA-256) before anything is deleted.
public sealed class BackupService
{
    private static readonly JsonSerializerOptions Opts = new() { WriteIndented = true };

    public List<BackupEntry> LoadRaw()
    {
        try { return File.Exists(AppPaths.BackupIndex) ? JsonSerializer.Deserialize<List<BackupEntry>>(File.ReadAllText(AppPaths.BackupIndex)) ?? new() : new(); }
        catch (Exception ex) { Logger.Error("Backup index unreadable", ex); return new(); }
    }
    private void Save(List<BackupEntry> list)
    {
        Directory.CreateDirectory(AppPaths.Backups);
        File.WriteAllText(AppPaths.BackupIndex, JsonSerializer.Serialize(list, Opts));
    }
    public List<BackupEntry> Load()
    {
        var l = LoadRaw(); foreach (var e in l) Verify(e);
        return l.OrderByDescending(e => e.Timestamp).ToList();
    }
    public void Verify(BackupEntry e)
    {
        try { e.Integrity = !File.Exists(e.FilePath) ? "File missing" : Hash(e.FilePath) == e.Sha256 ? "Verified" : "Checksum mismatch"; }
        catch { e.Integrity = "Unreadable"; }
    }
    private static string Hash(string p) { using var s = File.OpenRead(p); return Convert.ToHexString(SHA256.HashData(s)); }

    public BackupEntry? BackupKey(string keyPath)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.Backups);
            var id = Guid.NewGuid().ToString("N")[..8];
            var file = Path.Combine(AppPaths.Backups, $"{DateTime.Now:yyyyMMdd-HHmmss}-{id}.reg");
            if (!RunReg(new[] { "export", keyPath, file, "/y" }, out var msg)) { Logger.Error($"Backup failed for {keyPath}: {msg}"); return null; }
            if (!File.Exists(file) || new FileInfo(file).Length == 0 || !File.ReadAllText(file).StartsWith("Windows Registry Editor"))
            { Logger.Error($"Backup verification failed for {keyPath}"); return null; }
            var entry = new BackupEntry { Id = id, Timestamp = DateTime.Now, KeyPath = keyPath, FilePath = file, Sha256 = Hash(file), Integrity = "Verified" };
            var list = LoadRaw(); list.Add(entry); Save(list);
            Logger.Op($"Registry backup created: {keyPath} -> {file}");
            return entry;
        }
        catch (Exception ex) { Logger.Error($"Backup exception for {keyPath}", ex); return null; }
    }

    public bool Restore(BackupEntry e, out string message)
    {
        Verify(e);
        if (e.Integrity != "Verified") { message = "Backup file failed verification: " + e.Integrity; return false; }
        if (!RunReg(new[] { "import", e.FilePath }, out message)) { Logger.Error($"Restore failed for {e.KeyPath}: {message}"); return false; }
        var list = LoadRaw(); var m = list.FirstOrDefault(x => x.Id == e.Id);
        if (m != null) { m.RestoredAt = DateTime.Now; Save(list); }
        e.RestoredAt = DateTime.Now;
        Logger.Op($"Registry backup restored: {e.KeyPath} <- {e.FilePath}");
        return true;
    }

    private static bool RunReg(string[] args, out string output)
    {
        var psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "reg.exe"))
        { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var o = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
        if (!p.WaitForExit(30000)) { try { p.Kill(); } catch { } output = "reg.exe timed out"; return false; }
        output = o.Trim(); return p.ExitCode == 0;
    }
}
