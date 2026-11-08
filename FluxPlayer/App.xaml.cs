using FluxPlayer.Models;
using FluxPlayer.Services;
using FluxPlayer.ViewModels;
using FluxPlayer.Windows;
using Microsoft.Extensions.DependencyInjection;
using System.Windows;
using System.Windows.Input;

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
        if (!startupSettings.AllowMultipleInstances)
        {
            _singleInstance = SingleInstance.TryOwn(AppProfile.Current.DataDirectory, AppProfile.Current.Sources, AppProfile.Current.Subtitles, AppProfile.Current.Audio);
            if (_singleInstance == null) { Shutdown(0); return; }
        }
        ThemeService.ApplyDialogPalette(startupSettings.Theme == AppTheme.LightMica);

        bool adaptiveCache = FeatureCatalog.IsEnabled(startupSettings, FeatureCatalog.AdaptiveCacheBySource);
        bool ultraFastStartup = FeatureCatalog.IsEnabled(startupSettings, FeatureCatalog.UltraFastStartup);
        bool localTelemetry = FeatureCatalog.IsEnabled(startupSettings, FeatureCatalog.LocalPerformanceTelemetry);
        bool resumePerFile = FeatureCatalog.IsEnabled(startupSettings, FeatureCatalog.ResumePlaybackPerFile);

        var effectiveHardwareDecoding = startupSettings.HardwareDecoding;
        string selectedDecoder = startupSettings.Enhancements.GpuPolicy.Resolve(GpuRenderPolicy.Detect(), effectiveHardwareDecoding);
        effectiveHardwareDecoding &= selectedDecoder != "none";

        string? effectiveVideoOutput = null;

        services.AddSingleton(settingsService);
        services.AddSingleton(new MediaService(
            effectiveHardwareDecoding,
            effectiveVideoOutput,
            adaptiveCacheBySource: adaptiveCache,
            ultraFastStartup: ultraFastStartup,
            enableDiagnosticLogging: localTelemetry,
            resumePlaybackPerFile: resumePerFile,
            subtitleStyle: SubtitleStyle.Capture(startupSettings), hardwareDecoder: selectedDecoder, wpfSubtitleComposition: true));
        services.AddSingleton<MainViewModel>(sp => new MainViewModel(
            sp.GetRequiredService<MediaService>(),
            sp.GetRequiredService<SettingsService>(),
            startupSettings));

        services.AddSingleton<MainWindow>();
        services.AddTransient<MiniPlayerWindow>();
        services.AddTransient<FluxPlayer.Windows.SettingsWindow>();

        Services = services.BuildServiceProvider();

        // Every window (including tool windows built in code) is translated once it is loaded, and again when the language changes.
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler((sender, _) => Localizer.Apply((Window)sender)));
        // Esc closes auxiliary windows the way Windows dialogs do. Windows that give Esc another meaning (leave fullscreen, cancel an edit, close a drop-down) keep their own handling.
        EventManager.RegisterClassHandler(typeof(Window), Keyboard.KeyDownEvent, new KeyEventHandler((sender, e) =>
        {
            if (e.Key != Key.Escape || e.Handled || Keyboard.Modifiers != ModifierKeys.None) return;
            if (sender is not Window window || window == Current.MainWindow
                || window is MiniPlayerWindow or FluxPlayer.Windows.SettingsWindow or CalibrationWindow or CinemaTheaterWindow or PhotoViewerWindow or EqualizerWindow or HotkeyCaptureWindow or UrlInputWindow) return;
            if (e.OriginalSource is System.Windows.Controls.Primitives.TextBoxBase { AcceptsReturn: true }) return;
            window.Dispatcher.BeginInvoke(() => { if (window.IsLoaded && window.IsVisible) window.Close(); }, System.Windows.Threading.DispatcherPriority.Background);
            e.Handled = true;
        }));
        Localizer.Changed += (_, _) => Dispatcher.BeginInvoke(() => { foreach (Window window in Windows) Localizer.Apply(window); });
        var mainWindow = Services.GetRequiredService<MainWindow>();
        mainWindow.Show();
        _singleInstance?.ListenForLaunches(launch => Dispatcher.BeginInvoke(() => OpenFromOtherInstance(mainWindow, launch)));

        if (AppProfile.Current.Sources.Length > 0)
        {
            var vm = Services.GetRequiredService<MainViewModel>();
            var validFiles = AppProfile.Current.Sources
                .Where(path => System.IO.File.Exists(path) || MediaFormats.IsNetwork(path))
                .ToArray();
            if (validFiles.Length > 0)
            {
                vm.Engine.QueueExternalSources(AppProfile.Current.Subtitles ?? [], AppProfile.Current.Audio ?? []);
                vm.AddFilesToPlaylist(validFiles, playFirst: true);
            }
        }
        else Services.GetRequiredService<MainViewModel>().RestorePlaylist();
    }

    private SingleInstance? _singleInstance;

    private static void OpenFromOtherInstance(Window window, SingleInstance.LaunchRequest launch)
    {
        var files = launch.Sources.Where(path => System.IO.File.Exists(path) || System.IO.Directory.Exists(path) || MediaFormats.IsNetwork(path)).ToArray();
        if (files.Length > 0)
        {
            var vm = Services.GetRequiredService<MainViewModel>();
            vm.Engine.QueueExternalSources(launch.Subtitles, launch.Audio);
            vm.AddFilesToPlaylist(files, playFirst: true);
        }
        if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
        window.Show(); window.Activate();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstance?.Dispose();

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
