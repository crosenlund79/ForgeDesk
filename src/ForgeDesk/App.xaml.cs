using System.IO;
using System.Windows;
using System.Windows.Threading;
using ForgeDesk.Browser;
using ForgeDesk.Core;
using ForgeDesk.Core.Configuration;
using ForgeDesk.Infrastructure;
using ForgeDesk.Views;

namespace ForgeDesk;

public partial class App : Application
{
    private SingleInstance? _singleInstance;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var options = StartupOptions.Parse(e.Args);
        var paths = AppPaths.Resolve(options.DataDirectory);
        try
        {
            paths.EnsureCreated();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"ForgeDesk can't create its data folder:\n{paths.Root}\n\n{ex.Message}", "ForgeDesk", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(2);
            return;
        }

        Log.Initialize(paths.LogDirectory);
        Log.Info($"ForgeDesk {typeof(App).Assembly.GetName().Version} starting (data: {paths.Root}{(options.SmokeTestOutput is null ? string.Empty : ", smoke test")}).");

        if (options.SmokeTestOutput is null)
        {
            _singleInstance = new SingleInstance(paths.Root);
            if (!_singleInstance.IsFirstInstance)
            {
                Log.Info("Another ForgeDesk is running; asking it to come to the front.");
                _singleInstance.SignalFirstInstance();
                Shutdown(0);
                return;
            }
        }

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) => Log.Error("Unhandled exception", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error("Unobserved task exception", args.Exception);
            args.SetObserved();
        };

        BrowserEnvironment.UserDataFolder = paths.BrowserDataDirectory;
        var store = new ConfigStore(paths.ConfigDirectory);
        var load = store.Load();
        Log.Info($"Settings: {load.Outcome}, {load.Config.Projects.Count} projects.");
        var workspace = new Workspace(load.Config);
        if (options.SmokeTestOutput is not null)
        {
            SmokeTest.Seed(workspace);
        }

        ThemeManager.Apply(workspace.Preferences.Theme);

        var window = new MainWindow(workspace, store, paths, load.Warnings, quiet: options.SmokeTestOutput is not null);
        MainWindow = window;
        window.Show();

        _singleInstance?.ListenForActivation(() => Dispatcher.BeginInvoke(window.BringToFront));

        if (options.SmokeTestOutput is { } output)
        {
            _ = RunSmokeTestAsync(window, output);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.Info($"ForgeDesk exiting ({e.ApplicationExitCode}).");
        _singleInstance?.Dispose();
        base.OnExit(e);
    }

    private async Task RunSmokeTestAsync(MainWindow window, string output)
    {
        var code = await SmokeTest.RunAsync(window, output);
        Shutdown(code);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error("Unexpected error", e.Exception);
        e.Handled = true;
        try
        {
            ChoiceDialog.Info(MainWindow, "Something went wrong",
                "ForgeDesk ran into an unexpected problem but will keep running. Details were written to the log.",
                $"{e.Exception.GetType().Name}: {e.Exception.Message}");
        }
        catch (Exception)
        {
            // Never let error reporting itself crash the app.
        }
    }
}

/// <summary>Command-line options: --data-dir &lt;folder&gt; and --smoke-test &lt;output folder&gt;.</summary>
internal sealed record StartupOptions(string? DataDirectory, string? SmokeTestOutput)
{
    public static StartupOptions Parse(IReadOnlyList<string> args)
    {
        string? dataDir = null;
        string? smoke = null;
        for (var i = 0; i < args.Count; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--data-dir" when i + 1 < args.Count:
                    dataDir = args[++i];
                    break;
                case "--smoke-test" when i + 1 < args.Count:
                    smoke = Path.GetFullPath(args[++i]);
                    break;
            }
        }

        if (smoke is not null && dataDir is null)
        {
            dataDir = Path.Combine(smoke, "data");
        }

        return new StartupOptions(dataDir, smoke);
    }
}
