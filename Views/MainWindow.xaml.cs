using System.Windows.Documents;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Ukebook.Models;
using Ukebook.Services;
using Ukebook.ViewModels;

namespace Ukebook.Views;

public partial class MainWindow : Window
{
    private MainViewModel VM => (MainViewModel)DataContext;
    private readonly ChordProParser _parser = new();

    private static readonly Regex SyntaxTokenRegex =
        new(@"\[[^\]]*\]|\{[^}]*\}", RegexOptions.Compiled);

    /// <summary>
    /// Sdílené prostředí WebView2 s uživatelskou složkou v LocalApplicationData.
    /// Výchozí umístění profilu může na některých systémech skončit chybou „Access is denied“.
    /// </summary>
    private static Task<CoreWebView2Environment>? _webViewEnvironmentTask;

    private static Task<CoreWebView2Environment> GetWebViewEnvironmentAsync()
    {
        return _webViewEnvironmentTask ??= CreateWebViewEnvironmentAsync();
    }

    private static async Task<CoreWebView2Environment> CreateWebViewEnvironmentAsync()
    {
        var userDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Ukebook",
            "WebView2");
        Directory.CreateDirectory(userDataFolder);
        return await CoreWebView2Environment.CreateAsync(null, userDataFolder);
    }

    private bool _mainWebReady    = false;
    private bool _previewWebReady = false;

    private readonly DispatcherTimer _previewTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(600)
    };

    public MainWindow()
    {
        InitializeComponent();
        _ = InitWebViewsAsync();
        SetupKeyBindings();
        _previewTimer.Tick += (_, _) => { _previewTimer.Stop(); UpdatePreview(); };

        // Napojit ToggleThemeCommand přímo zde — máme jistotu že DataContext existuje
        VM.ThemeToggleRequested += OnThemeToggleRequested;
    }

    private void OnThemeToggleRequested()
    {
        ThemeService.Toggle();
        SettingsService.Current.IsDarkTheme = ThemeService.IsDark;
        SettingsService.Save();
        // Překresli HTML okno s novým tématem
        RenderMain();
        if (VM.IsEditing)
        {
            UpdatePreview();
            UpdateSyntaxHighlight();
        }
    }

    private async Task InitWebViewsAsync()
    {
        try
        {
            var env = await GetWebViewEnvironmentAsync();
            await SongWebView.EnsureCoreWebView2Async(env);
            _mainWebReady = true;

            await PreviewWebView.EnsureCoreWebView2Async(env);
            _previewWebReady = true;

            VM.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is nameof(MainViewModel.CurrentHtml))
                    RenderMain();
                else if (e.PropertyName is nameof(MainViewModel.IsEditing) && VM.IsEditing)
                {
                    // Při vstupu do editace překresli náhled s aktuálním motivem —
                    // TextChanged nemusí fire-nout, když je EditContent stejné jako posledně.
                    UpdatePreview();
                    UpdateSyntaxHighlight();
                }
            };

            RenderMain();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Nepodařilo se inicializovat WebView2.\n\n{ex.Message}\n\n" +
                "Zkontrolujte, zda je nainstalován Microsoft Edge WebView2 Runtime.",
                "Chyba inicializace", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void RenderMain()
    {
        if (_mainWebReady && VM.IsViewing)
            SongWebView.NavigateToString(VM.CurrentHtml);
    }

    private void Editor_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateLineNumbers();
        UpdateSyntaxHighlight();
        _previewTimer.Stop();
        _previewTimer.Start();
    }

    private void Editor_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (Math.Abs(e.VerticalChange) > 0)
            LineNumberGutter.ScrollToVerticalOffset(e.VerticalOffset);

        SyntaxOverlayTransform.X = -e.HorizontalOffset;
        SyntaxOverlayTransform.Y = -e.VerticalOffset;
    }

    private void UpdateSyntaxHighlight()
    {
        var text    = EditorTextBox.Text ?? string.Empty;
        var inlines = EditorSyntaxOverlay.Inlines;
        inlines.Clear();

        var chordBrush     = TryFindResource("EditorChordBrush")     as Brush ?? Brushes.SteelBlue;
        var directiveBrush = TryFindResource("EditorDirectiveBrush") as Brush ?? Brushes.Purple;
        var sectionBrush   = TryFindResource("EditorSectionBrush")   as Brush ?? Brushes.Firebrick;
        var commentBrush   = TryFindResource("EditorCommentBrush")   as Brush ?? Brushes.ForestGreen;

        int pos = 0;
        foreach (Match m in SyntaxTokenRegex.Matches(text))
        {
            if (m.Index > pos)
                inlines.Add(new Run(text[pos..m.Index]));

            var token = m.Value;
            var run   = new Run(token);

            if (token[0] == '[')
            {
                run.Foreground = chordBrush;
                run.FontWeight = FontWeights.SemiBold;
            }
            else // '{' directive
            {
                var inner = token.Length > 2 ? token[1..^1] : string.Empty;
                var name  = inner.Split(':', 2)[0].Trim().ToLowerInvariant();

                switch (name)
                {
                    case "start_of_verse"  or "sov" or "end_of_verse"  or "eov"
                      or "start_of_chorus" or "soc" or "end_of_chorus" or "eoc"
                      or "start_of_bridge" or "sob" or "end_of_bridge" or "eob"
                      or "start_of_tab"    or "sot" or "end_of_tab"    or "eot"
                      or "chorus":
                        run.Foreground = sectionBrush;
                        run.FontWeight = FontWeights.SemiBold;
                        break;
                    case "comment" or "c" or "comment_italic" or "ci":
                        run.Foreground = commentBrush;
                        run.FontStyle  = FontStyles.Italic;
                        break;
                    default:
                        run.Foreground = directiveBrush;
                        break;
                }
            }

            inlines.Add(run);
            pos = m.Index + m.Length;
        }

        if (pos < text.Length)
            inlines.Add(new Run(text[pos..]));
    }

    private void UpdateLineNumbers()
    {
        var lineCount = EditorTextBox.LineCount;
        if (lineCount < 1)
        {
            var text = EditorTextBox.Text ?? string.Empty;
            lineCount = 1;
            foreach (var ch in text) if (ch == '\n') lineCount++;
        }
        var sb = new StringBuilder(lineCount * 4);
        for (int i = 1; i <= lineCount; i++)
        {
            sb.Append(i);
            if (i < lineCount) sb.Append('\n');
        }
        LineNumberGutter.Text = sb.ToString();
    }

    private void UpdatePreview()
    {
        if (!_previewWebReady || VM.SelectedSong is null) return;
        try
        {
            var tempSong = new Song
            {
                Title           = VM.SelectedSong.Title,
                Artist          = VM.SelectedSong.Artist,
                Genre           = VM.SelectedSong.Genre,
                Key             = VM.SelectedSong.Key,
                Capo            = VM.SelectedSong.Capo,
                ChordProContent = VM.EditContent
            };
            var settings = VM.IsDarkTheme ? DisplaySettings.DarkTheme() : new DisplaySettings();
            settings.FontSize = 14;
            PreviewWebView.NavigateToString(_parser.GenerateHtml(tempSong, settings));
        }
        catch { }
    }

    private void SetupKeyBindings()
    {
        InputBindings.Add(new KeyBinding(VM.NewSongCommand,       Key.N,        ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(VM.EditSongCommand,      Key.E,        ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(VM.SaveSongCommand,      Key.S,        ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(VM.CancelEditCommand,    Key.Escape,   ModifierKeys.None));
        InputBindings.Add(new KeyBinding(VM.FontIncreaseCommand,  Key.OemPlus,  ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(VM.FontDecreaseCommand,  Key.OemMinus, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(VM.RefreshCommand,       Key.F5,       ModifierKeys.None));

        // Ctrl+T — téma přepínáme přímo zde, ne přes VM command
        var toggleThemeGesture = new KeyBinding(VM.ToggleThemeCommand, Key.T, ModifierKeys.Control);
        InputBindings.Add(toggleThemeGesture);
    }

    private void MenuExit_Click(object sender, RoutedEventArgs e)
        => Application.Current.Shutdown();

    private void MenuAbout_Click(object sender, RoutedEventArgs e)
        => MessageBox.Show(
            """
            Ukulele Zpěvník  —  verze 1.0
            Napsáno v C# 14 / .NET 9 / WPF

            Formát ChordPro:
              [C]Text [G]písně [Am]s akordy
              {title: Název}  {artist: Interpret}
              {start_of_chorus} … {end_of_chorus}
              {start_of_verse: Sloka 1} … {end_of_verse}
            """,
            "O aplikaci", MessageBoxButton.OK, MessageBoxImage.Information);

    private void MenuChordProHelp_Click(object sender, RoutedEventArgs e)
    {
        const string helpHtml = """
            <!DOCTYPE html><html><head><meta charset='UTF-8'>
            <style>
              body{font-family:'Segoe UI',sans-serif;padding:24px;max-width:700px;
                   background:#FAFAF8;color:#2C2C2C;line-height:1.6}
              h1{color:#1565C0} h2{color:#1565C0;margin-top:24px;font-size:15px}
              code{background:#EEF2FF;color:#3F51B5;padding:2px 6px;border-radius:4px;font-family:Consolas;font-size:13px}
              pre{background:#1E1E2E;color:#CDD6F4;padding:16px;border-radius:8px;font-family:Consolas;font-size:13px}
              table{border-collapse:collapse;width:100%}
              th{background:#1565C0;color:white;padding:8px 12px;text-align:left}
              td{padding:7px 12px;border-bottom:1px solid #E0E0E0;font-size:13px}
            </style></head><body>
            <h1>📖 ChordPro formát</h1>
            <h2>Akordy v textu</h2>
            <pre>[C]Dnes [Am]ráno [F]vstávám [G]brzy</pre>
            <h2>Direktivy</h2>
            <table>
              <tr><th>Direktiva</th><th>Popis</th></tr>
              <tr><td><code>{title: Název}</code></td><td>Název písně</td></tr>
              <tr><td><code>{artist: Interpret}</code></td><td>Interpret</td></tr>
              <tr><td><code>{key: C}</code></td><td>Tónina</td></tr>
              <tr><td><code>{tempo: 120}</code></td><td>Tempo BPM</td></tr>
              <tr><td><code>{capo: 2}</code></td><td>Kapo na pražci</td></tr>
              <tr><td><code>{genre: Pop}</code></td><td>Žánr</td></tr>
            </table>
            <h2>Sekce</h2>
            <table>
              <tr><th>Direktiva</th><th>Popis</th></tr>
              <tr><td><code>{start_of_verse: Sloka 1}</code> / <code>{end_of_verse}</code></td><td>Sloka</td></tr>
              <tr><td><code>{start_of_chorus}</code> / <code>{end_of_chorus}</code></td><td>Refrén</td></tr>
              <tr><td><code>{start_of_bridge}</code> / <code>{end_of_bridge}</code></td><td>Bridge</td></tr>
              <tr><td><code>{comment: text}</code></td><td>Komentář</td></tr>
              <tr><td><code>{chorus}</code></td><td>Odkaz – zopakujte refrén</td></tr>
            </table>
            </body></html>
            """;

        var win = new Window
        {
            Title = "Nápověda – ChordPro formát",
            Width = 760, Height = 620,
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        var wv = new WebView2();
        win.Content = wv;
        win.Show();
        _ = LoadChordProHelpAsync(wv, helpHtml);
    }

    private async Task LoadChordProHelpAsync(WebView2 wv, string helpHtml)
    {
        try
        {
            var env = await GetWebViewEnvironmentAsync();
            await wv.EnsureCoreWebView2Async(env);
            wv.NavigateToString(helpHtml);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Nepodařilo se zobrazit nápovědu (WebView2).\n\n{ex.Message}",
                "Chyba", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
