using FluxPlayer.Services;
using FluxPlayer.Windows;
using System.Windows;

namespace FluxPlayer;

public partial class MainWindow
{
    private CinemaLibraryWindow? _movieCatalogWindow, _seriesCatalogWindow;
    private CinemaPeopleWindow? _peopleWindow;
    private ClassicalLibraryWindow? _classicalWindow;
    private CinemaTheaterWindow? _cinemaTheaterWindow;
    private bool _openingCinemaTheater;
    private void MovieCatalog_Click(object sender, RoutedEventArgs e)
    {
        if (_movieCatalogWindow != null) { _movieCatalogWindow.Activate(); return; }
        _movieCatalogWindow = new(this, _vm, "Movie"); _movieCatalogWindow.Closed += (_, _) => _movieCatalogWindow = null; _movieCatalogWindow.Show();
    }
    private void SeriesCatalog_Click(object sender, RoutedEventArgs e)
    {
        if (_seriesCatalogWindow != null) { _seriesCatalogWindow.Activate(); return; }
        _seriesCatalogWindow = new(this, _vm, "Episode"); _seriesCatalogWindow.Closed += (_, _) => _seriesCatalogWindow = null; _seriesCatalogWindow.Show();
    }
    private void PeopleCatalog_Click(object sender, RoutedEventArgs e)
    {
        if (_peopleWindow != null) { _peopleWindow.Activate(); return; }
        _peopleWindow = new(this, _vm); _peopleWindow.Closed += (_, _) => _peopleWindow = null; _peopleWindow.Show();
    }
    private void ClassicalCatalog_Click(object sender, RoutedEventArgs e)
    {
        if (_classicalWindow != null) { _classicalWindow.Activate(); return; }
        _classicalWindow = new(this, _vm); _classicalWindow.Closed += (_, _) => _classicalWindow = null; _classicalWindow.Show();
    }
    private async void CinemaTheater_Click(object sender, RoutedEventArgs e)
    {
        if (_cinemaTheaterWindow != null) { _cinemaTheaterWindow.Activate(); return; }
        if (_openingCinemaTheater) return; _openingCinemaTheater = true;
        try
        {
            var data = await _vm.Cinema.Snapshot(); if (!IsLoaded) return;
            var rows = CinemaCatalogService.Query(data, "", "All video", "", null, "Any status", "Title");
            _cinemaTheaterWindow = new(this, _vm, rows, data.PlayTrailers); _cinemaTheaterWindow.Closed += (_, _) => _cinemaTheaterWindow = null; _cinemaTheaterWindow.Show();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { if (IsLoaded) ShowActionOsd("\uE8D6", "Unable to open TV library: " + ex.Message); }
        finally { _openingCinemaTheater = false; }
    }
}
