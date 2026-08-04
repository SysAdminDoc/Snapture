using System.IO;
using System.Windows;
using System.Windows.Threading;
using Serilog;
using Serilog.Events;
using Snapture.App.Services;
using Snapture.Capture;

namespace Snapture.App;

public partial class App : Application
{
    public static AppHost? Host { get; private set; }

    protected override async void OnStartup(StartupEventArgs e)
    {
        if (MagnificationHelperHost.IsHelperRequest(e.Args))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            base.OnStartup(e);
            Environment.ExitCode = MagnificationHelperHost.Run(e.Args);
            Shutdown();
            return;
        }

        if (CliCommandLine.IsCliRequest(e.Args))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            CliCommandLine.AttachParentConsole();
            base.OnStartup(e);
            if (!CliCommandLine.TryParse(e.Args, out var command, out var error))
            {
                Console.Error.WriteLine(error);
                Environment.ExitCode = 2;
                Shutdown();
                return;
            }

            if (command.Kind is CliCommandKind.Help or CliCommandKind.Version)
            {
                Environment.ExitCode = command.Kind == CliCommandKind.Help
                    ? 0
                    : 0;
                if (command.Kind == CliCommandKind.Help)
                    Console.WriteLine(CliCommandLine.Usage);
                else
                    Console.WriteLine(typeof(App).Assembly.GetName().Version?.ToString(3) ?? "unknown");
                Shutdown();
                return;
            }

            ConfigureLogging(e.Args);
            try
            {
                Host = new AppHost();
                Environment.ExitCode = await Host.RunCliAsync(command).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"CLI failed: {ex.Message}");
                Environment.ExitCode = 1;
            }
            finally
            {
                Shutdown();
            }
            return;
        }

        AppIdentity.SetAumid();
        ConfigureLogging(e.Args);

        base.OnStartup(e);
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            Log.Fatal(args.ExceptionObject as Exception, "AppDomain.UnhandledException");
            WriteCrashDump(args.ExceptionObject as Exception);
            Log.CloseAndFlush();
        };
        DispatcherUnhandledException += (_, args) =>
        {
            Log.Fatal(args.Exception, "Dispatcher.UnhandledException");
            WriteCrashDump(args.Exception);
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

    private static void WriteCrashDump(Exception? ex)
    {
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Snapture", "crashes");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, $"crash_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Snapture crash — {DateTime.UtcNow:O}");
            sb.AppendLine($"Version: {typeof(App).Assembly.GetName().Version}");
            sb.AppendLine($"OS: {Environment.OSVersion}");
            sb.AppendLine($".NET: {Environment.Version}");
            sb.AppendLine($"Working set: {Environment.WorkingSet / (1024 * 1024)} MB");
            sb.AppendLine();
            sb.AppendLine(ex?.ToString() ?? "(no exception)");
            File.WriteAllText(path, sb.ToString());
            Log.Information("CrashDump.Written {Path}", path);
        }
        catch { }
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
            .Enrich.With<LogRedactionEnricher>()
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
