using System.Text;
using System.Windows;
using NeoUninstall.Models;

namespace NeoUninstall.Views;

public partial class PreviewWindow : Window
{
    public PreviewWindow(IReadOnlyList<LeftoverItem> items)
    {
        InitializeComponent();
        var folders = items.Where(i => i.Kind == LeftoverKind.Folder).ToList();
        var keys = items.Where(i => i.Kind == LeftoverKind.RegistryKey).ToList();
        Header.Text = $"Review {items.Count} item(s) before deletion";
        var sb = new StringBuilder();
        sb.AppendLine($"FOLDERS ({folders.Count}) — moved to the Recycle Bin");
        foreach (var f in folders) sb.AppendLine($"  [{f.ConfidenceText}] {f.Path}   ({f.SizeText}, {f.FileCount} files)");
        sb.AppendLine();
        sb.AppendLine($"REGISTRY KEYS ({keys.Count}) — exported to a verified .reg backup first; deleted only if the backup succeeds");
        foreach (var k in keys) sb.AppendLine($"  [{k.ConfidenceText}] {k.Path}");
        PreviewText.Text = sb.ToString();
    }
    private void Delete_Click(object sender, RoutedEventArgs e) => DialogResult = true;
    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
