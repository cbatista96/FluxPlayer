using FluxPlayer.Models;
using FluxPlayer.Services;
using Microsoft.Extensions.DependencyInjection;
using System.Windows;
using System.Windows.Input;
using Wpf.Ui.Controls;

namespace FluxPlayer.Windows;

public partial class SettingsWindow : FluentWindow
{
    public event EventHandler? SettingsSaved;

    public SettingsWindow()
    {
        InitializeComponent();

        EmbeddedSettingsPanel.SettingsSaved += (_, _) =>
        {
            SettingsSaved?.Invoke(this, EventArgs.Empty);
            Close();
        };
        EmbeddedSettingsPanel.CloseRequested += (_, _) => Close();

        var settings = App.Services.GetRequiredService<SettingsService>().Load();
        SourceInitialized += (_, _) => ThemeService.Apply(settings.Theme, this);

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Close();
        };
    }

    public void ReloadFromDisk() => EmbeddedSettingsPanel.Reload();

    public void SyncTheme(AppTheme theme)
    {
        ThemeService.Apply(theme, this);
        EmbeddedSettingsPanel.SyncTheme(theme);
    }

    public void ShowAboutPage() => EmbeddedSettingsPanel.ShowAboutPage();
}
