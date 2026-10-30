using System.IO;
using System.Windows;

namespace Ritmo;
public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        WindowsIntegration.Register();
        DispatcherUnhandledException += (_, args) => {
            try { File.AppendAllText(Path.Combine(Store.Root, "errors.log"), DateTime.Now + " " + args.Exception + Environment.NewLine); } catch { }
            MessageBox.Show("No se pudo completar la operación. " + args.Exception.Message, "Ritmo");
            args.Handled = true;
        };
        if (e.Args.Contains("--self-test")) { SelfTest.Run(); Shutdown(); return; }
        if (e.Args.Contains("--network-test")) { await NetworkTest.Run(); Shutdown(); return; }
        MainWindow = new MainWindow(); MainWindow.Show();
    }
}
