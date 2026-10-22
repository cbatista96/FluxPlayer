using FluxPlayer.Models;
using FluxPlayer.Services;
using FluxPlayer.ViewModels;
using FluxPlayer.Windows;
using Microsoft.Extensions.DependencyInjection;
using System.Windows;

namespace FluxPlayer;

public partial class App : Application
{

    public static IServiceProvider Services { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {

        if (e.Args.Length == 1 &&
            string.Equals(e.Args[0], "--build-plugin-cache", StringComparison.OrdinalIgnoreCase))
        {
            bool ok = MediaService.BuildPluginCacheAndExit();
            Shutdown(ok ? 0 : 1);
            return;
        }

        try
        {
            AppProfile.Current = AppProfile.Parse(e.Args, AppContext.BaseDirectory,
                portableMarker: System.IO.File.Exists(System.IO.Path.Combine(AppContext.BaseDirectory, "portable.flag")));
            System.IO.Directory.CreateDirectory(AppProfile.Current.DataDirectory);
        }
        catch (Exception ex) when (ex is ArgumentException or System.IO.IOException or UnauthorizedAccessException or NotSupportedException)
        {
            MessageBox.Show(ex.Message, "FluxPlayer startup options", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(2); return;
        }

        try
        {
            var profileDir = AppProfile.Current.CacheDirectory;
            System.IO.Directory.CreateDirectory(profileDir);
            System.Runtime.ProfileOptimization.SetProfileRoot(profileDir);
            System.Runtime.ProfileOptimization.StartProfile("startup.jitprofile");
        }
        catch { }

        InstallCrashHandlers();

        base.OnStartup(e);

        var services = new ServiceCollection();

        var settingsService = new SettingsService();
        var startupSettings = settingsService.Load();
        ThemeService.ApplyDialogPalette(startupSettings.Theme == AppTheme.LightMica);

        bool adaptiveCache = FeatureCatalog.IsEnabled(startupSettings, FeatureCatalog.AdaptiveCacheBySource);
        bool ultraFastStartup = FeatureCatalog.IsEnabled(startupSettings, FeatureCatalog.UltraFastStartup);
        bool localTelemetry = FeatureCatalog.IsEnabled(startupSettings, FeatureCatalog.LocalPerformanceTelemetry);
        bool resumePerFile = FeatureCatalog.IsEnabled(startupSettings, FeatureCatalog.ResumePlaybackPerFile);

        var effectiveHardwareDecoding = startupSettings.HardwareDecoding;

        string? effectiveVideoOutput = null;

        services.AddSingleton(settingsService);
        services.AddSingleton(new MediaService(
            effectiveHardwareDecoding,
            effectiveVideoOutput,
            adaptiveCacheBySource: adaptiveCache,
            ultraFastStartup: ultraFastStartup,
            enableDiagnosticLogging: localTelemetry,
            resumePlaybackPerFile: resumePerFile));
        services.AddSingleton<MainViewModel>(sp => new MainViewModel(
            sp.GetRequiredService<MediaService>(),
            sp.GetRequiredService<SettingsService>(),
            startupSettings));

        services.AddSingleton<MainWindow>();
        services.AddTransient<MiniPlayerWindow>();
        services.AddTransient<FluxPlayer.Windows.SettingsWindow>();

        Services = services.BuildServiceProvider();

        var mainWindow = Services.GetRequiredService<MainWindow>();
        mainWindow.Show();

        if (AppProfile.Current.Sources.Length > 0)
        {
            var vm = Services.GetRequiredService<MainViewModel>();
            var validFiles = AppProfile.Current.Sources
                .Where(path => System.IO.File.Exists(path) || MediaFormats.IsNetwork(path))
                .ToArray();
            if (validFiles.Length > 0)
                vm.AddFilesToPlaylist(validFiles, playFirst: true);
        }
        else Services.GetRequiredService<MainViewModel>().RestorePlaylist();
    }

    protected override void OnExit(ExitEventArgs e)
    {

        if (Services != null)
        {
            Task.Run(async () => await Services.GetRequiredService<MainViewModel>().ShutdownAsync()).GetAwaiter().GetResult();
            Services.GetService<MediaService>()?.Dispose();
        }
        base.OnExit(e);
    }

    private static int _crashDialogs;

    private void InstallCrashHandlers()
    {
        DispatcherUnhandledException += (_, args) =>
        {
            LogCrash("UI", args.Exception);
            args.Handled = true;
            if (Interlocked.Increment(ref _crashDialogs) == 1)
            {
                try
                {
                    MessageBox.Show(
                        "FluxPlayer recovered from an unexpected error.\n\n" +
                        args.Exception.Message + "\n\nDetails: " + CrashLogPath,
                        "FluxPlayer", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                catch { }
                Interlocked.Exchange(ref _crashDialogs, 0);
            }
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            LogCrash(args.IsTerminating ? "FATAL" : "AppDomain", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            LogCrash("Task", args.Exception);
            args.SetObserved();
        };
    }

    private static string CrashLogPath => System.IO.Path.Combine(AppProfile.Current.CacheDirectory, "logs", "crash.log");

    private static void LogCrash(string kind, Exception? ex)
    {
        try
        {
            var path = CrashLogPath;
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            var info = new System.IO.FileInfo(path);
            if (info.Exists && info.Length > 1_000_000) info.Delete();
            System.IO.File.AppendAllText(path,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {kind} v{typeof(App).Assembly.GetName().Version}{Environment.NewLine}{ex}{Environment.NewLine}{Environment.NewLine}");
        }
        catch { }
    }
}
