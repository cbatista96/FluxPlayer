using FluxPlayer.Models;
using FluxPlayer.Services;
using FluxPlayer.ViewModels;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Wpf.Ui.Controls;

namespace FluxPlayer.Windows;

public partial class AudioOutputProfilesWindow : FluentWindow
{
    private readonly MainViewModel _vm;
    private bool _refreshing;
    private sealed record OutputChoice(AudioDevice Device, bool Available)
    {
        public override string ToString() => Device.Name + (Available ? "" : " (disconnected)");
    }

    public AudioOutputProfilesWindow(Window owner, MainViewModel vm)
    {
        InitializeComponent(); Owner = owner; _vm = vm;
        var body = (UIElement)Content; Content = null; Content = ToolWindowChrome.Wrap(this, body);
        SourceInitialized += (_, _) => ThemeService.Apply(vm.CurrentTheme, this);
        RefreshDevices();
        Activated += (_, _) => ShowDetails();
    }

    private void RefreshDevices()
    {
        string selected = (DeviceBox.SelectedItem as OutputChoice)?.Device.Id ?? _vm.SelectedOutputDeviceId;
        _vm.RefreshOutputDevices();
        var choices = _vm.OutputDevices.Select(device => new OutputChoice(device, true)).ToList();
        foreach (var pair in _vm.AudioOutputProfiles)
            if (!choices.Any(choice => string.Equals(choice.Device.Id, pair.Key, StringComparison.OrdinalIgnoreCase)))
                choices.Add(new OutputChoice(new AudioDevice(pair.Key, pair.Value.DeviceName), false));
        _refreshing = true;
        DeviceBox.ItemsSource = choices;
        DeviceBox.SelectedItem = choices.FirstOrDefault(choice => string.Equals(choice.Device.Id, selected, StringComparison.OrdinalIgnoreCase)) ?? choices.FirstOrDefault();
        _refreshing = false; ShowDetails();
    }

    private void ShowDetails()
    {
        if (_vm == null) return;
        AutomaticBox.IsChecked = _vm.AutomaticallyApplyAudioOutputProfiles;
        CurrentOutputText.Text = "Current output: " + (_vm.OutputDevices.FirstOrDefault(device => device.Id == _vm.SelectedOutputDeviceId)?.Name
            ?? (_vm.AudioOutputProfiles.TryGetValue(_vm.SelectedOutputDeviceId, out var current) ? current.DeviceName : _vm.SelectedOutputDeviceId));
        if (DeviceBox.SelectedItem is not OutputChoice choice) return;
        bool saved = _vm.AudioOutputProfiles.TryGetValue(choice.Device.Id, out var profile);
        ApplyButton.IsEnabled = choice.Available && saved; RemoveButton.IsEnabled = saved;
        ProfileDetails.Text = profile == null ? "No profile saved for this output. Saving captures the player's current audio settings."
            : string.Create(CultureInfo.InvariantCulture, $"Volume: {profile.Volume:0.#}%\nEqualizer: {(profile.EqualizerEnabled ? "On" : "Off")} · {profile.Preset} · preamp {profile.Preamp:+0.#;-0.#;0} dB\nBands (31 Hz → 16 kHz): {string.Join(" / ", profile.Bands.Select(value => value.ToString("+0.#;-0.#;0", CultureInfo.InvariantCulture)))} dB\nPeak compensation: {(profile.AntiClip ? "On" : "Off")}\nAudio delay: {profile.AudioDelayMs:+0;-0;0} ms");
    }

    private void Device_Changed(object sender, SelectionChangedEventArgs e) { if (!_refreshing) ShowDetails(); }
    private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshDevices();
    private void Automatic_Click(object sender, RoutedEventArgs e)
    {
        bool enabled = AutomaticBox.IsChecked == true;
        if (!_vm.SetAutomaticAudioOutputProfiles(enabled)) { FailedSave(); ShowDetails(); return; }
        StatusText.Text = enabled ? "Automatic profile application enabled. Current audio is unchanged until you choose an output or restart."
            : "Automatic profile application disabled. Saved profiles can still be applied explicitly.";
    }
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (DeviceBox.SelectedItem is not OutputChoice choice) return;
        try
        {
            if (!_vm.SaveAudioOutputProfile(choice.Device)) { FailedSave(); return; }
            ShowDetails(); StatusText.Text = $"Current audio settings saved for {choice.Device.Name}.";
        }
        catch (ArgumentException ex) { StatusText.Text = ex.Message; }
    }
    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (DeviceBox.SelectedItem is not OutputChoice { Available: true } choice) return;
        if (!_vm.SelectOutputDevice(choice.Device, true)) { FailedSave(); return; }
        ShowDetails(); StatusText.Text = $"Selected {choice.Device.Name} and applied its saved profile. Volume respects the player's boost limit.";
    }
    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (DeviceBox.SelectedItem is not OutputChoice choice) return;
        if (!_vm.RemoveAudioOutputProfile(choice.Device.Id)) { FailedSave(); return; }
        RefreshDevices(); StatusText.Text = "Saved profile removed. Current audio settings are retained.";
    }
    private void FailedSave() => StatusText.Text = "The change could not be saved: " + _vm.AudioOutputProfileSaveError;
}
