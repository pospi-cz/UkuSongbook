using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Ukebook.Models;

namespace Ukebook.Services;

public sealed class AppSettings
{
    public bool IsDarkTheme { get; set; }

    [JsonConverter(typeof(StringEnumConverter))]
    public SongViewMode ViewMode { get; set; } = SongViewMode.Text;
}

public static class SettingsService
{
    private static readonly string AppDataPath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Ukebook");
    private static readonly string SettingsPath = Path.Combine(AppDataPath, "settings.json");

    public static AppSettings Current { get; } = LoadFromDisk();

    private static AppSettings LoadFromDisk()
    {
        try
        {
            if (File.Exists(SettingsPath))
                return JsonConvert.DeserializeObject<AppSettings>(File.ReadAllText(SettingsPath)) ?? new AppSettings();
        }
        catch { }
        return new AppSettings();
    }

    public static void Save()
    {
        Directory.CreateDirectory(AppDataPath);
        File.WriteAllText(SettingsPath, JsonConvert.SerializeObject(Current, Formatting.Indented));
    }
}
