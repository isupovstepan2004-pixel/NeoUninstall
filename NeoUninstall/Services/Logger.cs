namespace NeoUninstall.Services;

public static class AppPaths
{
    public static readonly string Root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NeoUninstall");
    public static string Logs => Path.Combine(Root, "logs");
    public static string History => Path.Combine(Root, "history.json");
    public static string Backups => Path.Combine(Root, "backups");
    public static string BackupIndex => Path.Combine(Backups, "index.json");
    public static string OpsLog => Path.Combine(Logs, "operations.log");
}

public static class Logger
{
    private static readonly object Lock = new();
    public static void Info(string m) => Write("INFO", m, false);
    public static void Warn(string m) => Write("WARN", m, false);
    public static void Error(string m, Exception? ex = null) => Write("ERROR", ex == null ? m : $"{m}: {ex}", false);
    /// Records a user-visible operation (deletion, backup, restore, uninstall). Goes to both logs.
    public static void Op(string m) => Write("OP", m, true);

    private static void Write(string level, string msg, bool ops)
    {
        try
        {
            lock (Lock)
            {
                Directory.CreateDirectory(AppPaths.Logs);
                var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {msg}{Environment.NewLine}";
                File.AppendAllText(Path.Combine(AppPaths.Logs, $"neouninstall-{DateTime.Now:yyyyMMdd}.log"), line);
                if (ops) File.AppendAllText(AppPaths.OpsLog, line);
            }
        }
        catch { /* logging must never crash the app */ }
    }
}
