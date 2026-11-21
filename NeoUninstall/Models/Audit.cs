using Microsoft.Win32;
using NeoUninstall.Infrastructure;

namespace NeoUninstall.Models;

public sealed class StartupEntry : ObservableObject
{
    private bool _enabled = true;
    public string Name { get; init; } = "";
    public string Command { get; init; } = "";
    public string ExePath { get; init; } = "";
    public string Publisher { get; init; } = "";
    public string Source { get; init; } = "";
    public string Location { get; init; } = "";
    public RegistryHive ApprovedHive { get; init; }
    public string ApprovedSub { get; init; } = "";
    public string ApprovedValueName { get; init; } = "";
    public bool TargetMissing { get; init; }
    public string Association { get; init; } = "";
    public string Evidence { get; init; } = "";
    public bool IsEnabled { get => _enabled; set { if (Set(ref _enabled, value)) Raise(nameof(StatusText)); } }
    public string StatusText => !_enabled ? "Disabled" : TargetMissing ? "Enabled · target missing" : "Enabled";
}

public sealed class TaskEntry
{
    public string Name { get; init; } = "";
    public string TaskPath { get; init; } = "";
    public string State { get; init; } = "";
    public string Author { get; init; } = "";
    public string ExePath { get; init; } = "";
    public string Arguments { get; init; } = "";
    public string Publisher { get; init; } = "";
    public string LastRun { get; init; } = "";
    public string NextRun { get; init; } = "";
    public bool IsMicrosoft { get; init; }
    public string Association { get; init; } = "";
    public string Evidence { get; init; } = "";
    public bool Flagged { get; init; }
    public string Finding => Flagged ? "Possibly orphaned" : "";
}

public sealed class ServiceEntry
{
    public string Name { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string Publisher { get; init; } = "";
    public string ExePath { get; init; } = "";
    public string StartMode { get; init; } = "";
    public string Status { get; init; } = "";
    public bool IsSystem { get; init; }
    public string Association { get; init; } = "";
    public string Evidence { get; init; } = "";
    public bool Flagged { get; init; }
    public string Finding => Flagged ? "Possibly orphaned" : "";
}
