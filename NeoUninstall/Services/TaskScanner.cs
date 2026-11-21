using System.Collections;
using NeoUninstall.Models;

namespace NeoUninstall.Services;

/// Read-only enumeration through the Task Scheduler COM API (late-bound, no extra dependencies).
public static class TaskScanner
{
    public static List<TaskEntry> Scan(AppAssociator assoc, CancellationToken ct)
    {
        var list = new List<TaskEntry>();
        try
        {
            var t = Type.GetTypeFromProgID("Schedule.Service") ?? throw new InvalidOperationException("Task Scheduler COM class not available");
            dynamic svc = Activator.CreateInstance(t)!;
            svc.Connect();
            Walk(svc.GetFolder("\\"), assoc, list, ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { Logger.Error("Scheduled task scan failed", ex); }
        Logger.Info($"Task scan finished: {list.Count} tasks");
        return list.OrderBy(x => x.TaskPath, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static void Walk(dynamic folder, AppAssociator assoc, List<TaskEntry> list, CancellationToken ct)
    {
        try
        {
            foreach (dynamic task in (IEnumerable)folder.GetTasks(1))
            {
                ct.ThrowIfCancellationRequested();
                try { list.Add(Build(task, assoc)); }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { Logger.Warn("Task skipped: " + ex.Message); }
            }
            foreach (dynamic sub in (IEnumerable)folder.GetFolders(0)) Walk(sub, assoc, list, ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { Logger.Warn("Task folder skipped: " + ex.Message); }
    }

    private static TaskEntry Build(dynamic task, AppAssociator assoc)
    {
        string name = task.Name, path = task.Path;
        int state = (int)task.State;
        dynamic def = task.Definition;
        string author = "";
        try { author = (string)def.RegistrationInfo.Author ?? ""; } catch { }
        string exe = "", args = "";
        try
        {
            foreach (dynamic a in (IEnumerable)def.Actions)
                if ((int)a.Type == 0) { exe = (string)a.Path ?? ""; args = (string)a.Arguments ?? ""; break; }
        }
        catch { }
        exe = Environment.ExpandEnvironmentVariables(exe).Trim('"');
        bool rooted = exe.Length > 0 && Path.IsPathRooted(exe);
        bool missing = rooted && PathUtil.DriveReady(exe) && !File.Exists(exe);
        var pub = rooted && !missing ? PathUtil.Publisher(exe) : "";
        bool isMs = path.StartsWith("\\Microsoft\\", StringComparison.OrdinalIgnoreCase) || author.StartsWith("Microsoft", StringComparison.OrdinalIgnoreCase);
        var a2 = assoc.Find(exe, pub);

        string evidence; bool flagged = false;
        if (missing)
        {
            var dirGone = !Directory.Exists(Path.GetDirectoryName(exe) ?? "");
            evidence = $"Executable not found: {exe}" + (dirGone ? " (its folder no longer exists)." : ".");
            if (!isMs && a2 == null) { flagged = true; evidence += " No installed application could be linked to this task."; }
            else if (a2 != null) evidence += " " + a2.Evidence;
        }
        else evidence = a2?.Evidence ?? (exe.Length == 0 ? "Task has no plain executable action." : "No installed application could be linked to this task.");

        return new TaskEntry
        {
            Name = name, TaskPath = path, State = state switch { 0 => "Unknown", 1 => "Disabled", 2 => "Queued", 3 => "Ready", 4 => "Running", _ => "?" },
            Author = author, ExePath = exe, Arguments = args, Publisher = pub, IsMicrosoft = isMs,
            LastRun = Fmt(task.LastRunTime), NextRun = Fmt(task.NextRunTime),
            Association = a2?.AppName ?? "", Evidence = evidence, Flagged = flagged,
        };
    }

    private static string Fmt(object? v) => v is DateTime d && d.Year > 2000 ? d.ToString("yyyy-MM-dd HH:mm") : "—";
}
