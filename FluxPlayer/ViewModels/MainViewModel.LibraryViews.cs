using FluxPlayer.Models;

namespace FluxPlayer.ViewModels;

public partial class MainViewModel
{
    public LibraryColumnView? LibraryColumnView => _settings.LibraryColumnView;
    public IReadOnlyDictionary<string, LibraryColumnView> SavedLibraryColumnViews => _settings.LibraryColumnViews;
    public string? LibraryViewSaveError => _settingsService.LastError;
    public bool SaveLibraryColumnView(LibraryColumnView layout, string? name = null, string? removeName = null)
    {
        var normalized = LibraryColumnViews.Normalize(layout) ?? throw new ArgumentException("Invalid library column layout.");
        var settings = _settingsService.Load(); settings.LibraryColumnView = normalized;
        if (name != null)
        {
            name = name.Trim();
            if (name.Length is < 1 or > 80 || name.Any(char.IsControl)) throw new ArgumentException("Use 1–80 printable characters for the view name.");
            if (!settings.LibraryColumnViews.ContainsKey(name) && settings.LibraryColumnViews.Count >= 16) throw new ArgumentException("Save up to 16 column views.");
            settings.LibraryColumnViews[name] = normalized;
        }
        if (removeName != null) settings.LibraryColumnViews.Remove(removeName);
        if (!_settingsService.Save(settings))
        {
            ErrorOccurred?.Invoke(this, "Unable to save library column views: " + _settingsService.LastError);
            return false;
        }
        _settings.LibraryColumnView = settings.LibraryColumnView; _settings.LibraryColumnViews = settings.LibraryColumnViews; return true;
    }
}
