using FluxPlayer.Services;

namespace FluxPlayer.ViewModels;

public partial class MainViewModel
{
    private SonicLibrary? _sonic;
    public SonicLibrary Sonic => _sonic ??= new(_settingsService.DataDirectory);
}
