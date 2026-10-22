using CommunityToolkit.Mvvm.ComponentModel;

namespace FluxPlayer.Models;

public partial class MediaItem : ObservableObject
{
    [ObservableProperty] private string _filePath = string.Empty;
    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private string _duration = "--:--";
    [ObservableProperty] private bool _isPlaying = false;
    [ObservableProperty] private int _index = 0;
    [ObservableProperty] private string _artist = string.Empty;
    [ObservableProperty] private string _album = string.Empty;
    [ObservableProperty] private string _genre = string.Empty;
    [ObservableProperty] private long _durationMs;
    [ObservableProperty] private int _queuePosition = 0;

    public bool IsNetwork => MediaFormats.IsNetwork(FilePath);

    public bool IsAudio => MediaFormats.IsAudio(FilePath);

    public string Icon => IsNetwork ? "\uE774" : IsAudio ? "\uE8D6" : "\uE714";

    partial void OnFilePathChanged(string value)
    {
        OnPropertyChanged(nameof(IsNetwork));
        OnPropertyChanged(nameof(IsAudio));
        OnPropertyChanged(nameof(Icon));
    }
}
