using System.IO;
using System.Windows;
using System.Windows.Threading;
using Snapture.App.Services;

namespace Snapture.App;

public partial class App : Application
{
    public static AppHost? Host { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        AppDomain.CurrentDomain.UnhandledException += (_, args) => LogCrash(args.ExceptionObject as Exception);
        DispatcherUnhandledException += (_, args) => { LogCrash(args.Exception); args.Handled = true; };
        TaskScheduler.UnobservedTaskException += (_, args) => { LogCrash(args.Exception); args.SetObserved(); };

        Host = new AppHost();
        Host.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Host?.Dispose();
        base.OnExit(e);
    }

    private static void LogCrash(Exception? ex)
    {
        if (ex == null) return;
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Snapture");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "crashlog.txt");
            File.AppendAllText(path,
                $"[{DateTime.Now:O}] {ex.GetType().FullName}: {ex.Message}{Environment.NewLine}{ex.StackTrace}{Environment.NewLine}{Environment.NewLine}");
        }
        catch { /* swallow logging errors */ }
    }
}
