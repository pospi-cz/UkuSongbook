namespace Ukebook.Services;

public static class ThemeService
{
    private const string LightUri = "Themes/LightTheme.xaml";
    private const string DarkUri  = "Themes/DarkTheme.xaml";

    public static bool IsDark { get; private set; } = false;

    public static void Apply(bool dark)
    {
        IsDark = dark;
        var uri  = new Uri(dark ? DarkUri : LightUri, UriKind.Relative);
        var dict = new ResourceDictionary { Source = uri };

        var appDicts = Application.Current.Resources.MergedDictionaries;

        // Nejdřív přidat nové téma. Dříve se nejdřív mazalo staré a pak vkládalo nové —
        // krátce zůstával jen MainTheme.xaml bez barevných brushů a DynamicResource
        // hlásil „Resource not found“ pro všechny klíče z Light/DarkTheme.
        appDicts.Insert(0, dict);

        for (var i = appDicts.Count - 1; i >= 0; i--)
        {
            var d = appDicts[i];
            if (ReferenceEquals(d, dict)) continue;
            if (d.Source?.OriginalString is string s &&
                (s.EndsWith("LightTheme.xaml", StringComparison.OrdinalIgnoreCase) ||
                 s.EndsWith("DarkTheme.xaml", StringComparison.OrdinalIgnoreCase)))
                appDicts.RemoveAt(i);
        }
    }

    public static void Toggle() => Apply(!IsDark);
}
