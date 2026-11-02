using FluxPlayer.Models;
using FluxPlayer.Services;
using FluxPlayer.Windows;
using System.Windows;

namespace FluxPlayer;

public partial class MainWindow
{
    private ExternalControlWindow? _externalControlWindow;
    private AutomaticProfilesWindow? _automaticProfilesWindow;
    private ExperienceWindow? _experienceWindow;
    private CaptureWindow? _captureWindow;
    private TelevisionWindow? _televisionWindow;
    private WindowDock? _equalizerDock;
    private CancellationTokenSource? _idleBackgroundLoad;
    private readonly CinemaArtwork _idleArtwork = new();
    private void InitializeExternalExperience()
    {
        Loaded += async (_, _) => { try { await ApplyExperience(_vm.CurrentSettings.Experience ?? new()); } catch (Exception ex) when (ex is not OutOfMemoryException) { if (IsLoaded) ShowActionOsd("\uE8D6", "Appearance: " + ex.Message); } };
        Closed += (_, _) => { _idleBackgroundLoad?.Cancel(); _idleArtwork.Clear(); _equalizerDock?.Dispose(); };
    }
    private async Task ApplyExperience(ExperienceSettings settings)
    {
        settings.Validate();
        _idleBackgroundLoad?.Cancel(); var job = new CancellationTokenSource(); _idleBackgroundLoad = job;
        try { var image = await _idleArtwork.Load(settings.Background, 1600, job.Token); if (IsLoaded && !job.IsCancellationRequested) { ThemeService.SetCustomAccent(settings.Accent); UserIdleBackground.Opacity = settings.BackgroundOpacity; ConfigureEqualizerDock(); UserIdleBackground.Source = image; } }
        catch (OperationCanceledException) { }
        finally { if (ReferenceEquals(_idleBackgroundLoad, job)) _idleBackgroundLoad = null; job.Dispose(); }
    }
    private void ConfigureEqualizerDock()
    {
        _equalizerDock?.Dispose(); _equalizerDock = null; var settings = _vm.CurrentSettings.Experience ?? new();
        if (settings.DockEqualizer && _equalizerWindow is { IsLoaded: true } child) _equalizerDock = new(this, child, settings.DockSide);
    }
    private void ExternalControl_Click(object sender, RoutedEventArgs e)
    { if (_externalControlWindow != null) { _externalControlWindow.Activate(); return; } _externalControlWindow = new(this, _vm); _externalControlWindow.Closed += (_, _) => _externalControlWindow = null; _externalControlWindow.Show(); }
    private void AutomaticProfiles_Click(object sender, RoutedEventArgs e)
    { if (_automaticProfilesWindow != null) { _automaticProfilesWindow.Activate(); return; } _automaticProfilesWindow = new(this, _vm); _automaticProfilesWindow.Closed += (_, _) => _automaticProfilesWindow = null; _automaticProfilesWindow.Show(); }
    private void Experience_Click(object sender, RoutedEventArgs e)
    { if (_experienceWindow != null) { _experienceWindow.Activate(); return; } _experienceWindow = new(this, _vm, ApplyExperience); _experienceWindow.Closed += (_, _) => _experienceWindow = null; _experienceWindow.Show(); }
    private void Capture_Click(object sender, RoutedEventArgs e)
    { if (_captureWindow != null) { _captureWindow.Activate(); return; } _captureWindow = new(this, _vm); _captureWindow.Closed += (_, _) => _captureWindow = null; _captureWindow.Show(); }
    private void Television_Click(object sender, RoutedEventArgs e)
    { if (_televisionWindow != null) { _televisionWindow.Activate(); return; } _televisionWindow = new(this, _vm); _televisionWindow.Closed += (_, _) => _televisionWindow = null; _televisionWindow.Show(); }
}
