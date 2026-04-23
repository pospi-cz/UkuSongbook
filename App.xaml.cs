using Ukebook.Services;

namespace Ukebook;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        SongService.EnsureDataDirectory();
        // Motiv načteme z settings.json v %APPDATA%\Ukebook; výchozí = světlý
        ThemeService.Apply(SettingsService.Current.IsDarkTheme);
    }
}
