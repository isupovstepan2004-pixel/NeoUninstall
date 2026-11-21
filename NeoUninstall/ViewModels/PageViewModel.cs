using NeoUninstall.Infrastructure;
namespace NeoUninstall.ViewModels;
public abstract class PageViewModel : ObservableObject
{
    public abstract string Title { get; }
    public abstract string Glyph { get; }
}
