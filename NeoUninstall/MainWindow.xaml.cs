using System.Windows;
using NeoUninstall.ViewModels;

namespace NeoUninstall;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        var vm = new MainViewModel();
        DataContext = vm;
        Loaded += async (_, _) => await vm.InitializeAsync();
    }
}
