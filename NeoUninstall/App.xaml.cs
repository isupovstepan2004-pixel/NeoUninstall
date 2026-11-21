using System.Windows;
using NeoUninstall.Services;

namespace NeoUninstall;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Logger.Info("NeoUninstall started");
        DispatcherUnhandledException += (_, a) =>
        {
            Logger.Error("Unhandled UI exception", a.Exception);
            MessageBox.Show("Unexpected error. Details were written to the log.\n\n" + a.Exception.Message, "NeoUninstall");
            a.Handled = true;
        };
    }
}
