using System.IO;
using System.Windows;
using System.Windows.Threading;
using Serilog;
using Serilog.Events;
using Snapture.App.Services;

namespace Snapture.App;

public partial class App : Application
{
    public static AppHost? Host { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        AppIdentity.SetAumid();
        ConfigureLogging(e.Args);

        base.OnStartup(e);
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            Log.Fatal(args.ExceptionObject as Exception, "AppDomain.UnhandledException");
            Log.CloseAndFlush();
        };
        DispatcherUnhandledException += (_, args) =>
        {
            Log.Fatal(args.Exception, "Dispatcher.UnhandledException");
            args.Handled = true;
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error(args.Exception, "Task.UnobservedException");
            args.SetObserved();
        };

        Log.Information("App.Startup");
        Host = new AppHost();
        Host.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.Information("App.Shutdown");
        Host?.Dispose();
        Log.CloseAndFlush();
        base.OnExit(e);
    }

    private static void ConfigureLogging(string[] args)
    {
        var logDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Snapture", "logs");

        var level = args.Contains("--verbose", StringComparer.OrdinalIgnoreCase)
            ? LogEventLevel.Debug
            : LogEventLevel.Information;

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Is(level)
            .WriteTo.File(
                Path.Combine(logDir, "snapture-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                buffered: true,
                flushToDiskInterval: TimeSpan.FromSeconds(1),
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();
    }
}
