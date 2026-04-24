using Ukebook.Models;
using Ukebook.Services;

namespace Ukebook.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly SongService    _songService = new();
    private readonly ChordProParser _parser      = new();

    private Song?        _selectedSong;
    private string       _searchText          = string.Empty;
    private string       _currentHtml         = string.Empty;
    private bool         _isDarkTheme         = ThemeService.IsDark;
    private int          _fontSize            = 16;
    private int          _transpose           = 0;
    private string       _statusMessage       = "Připraveno";
    private bool         _isEditing           = false;
    private string       _editContent         = string.Empty;
    private string       _selectedGenreFilter = "Vše";
    private SongViewMode _viewMode            = SettingsService.Current.ViewMode;

    public ObservableCollection<Song>   AllSongs      { get; } = [];
    public ObservableCollection<Song>   FilteredSongs { get; } = [];
    public ObservableCollection<string> Genres        { get; } = [];

    /// <summary>View se přihlásí na tento event a zavolá ThemeService.Toggle().</summary>
    public event Action? ThemeToggleRequested;

    public Song? SelectedSong
    {
        get => _selectedSong;
        set
        {
            _selectedSong = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSelectedSong));
            // Včetně value == null → úvodní obrazovka; při null dříve RenderSong() neběžel → prázdné WebView
            if (!_isEditing) RenderSong();
        }
    }

    public string SearchText
    {
        get => _searchText;
        set { _searchText = value; OnPropertyChanged(); FilterSongs(); }
    }

    public string CurrentHtml
    {
        get => _currentHtml;
        set { _currentHtml = value; OnPropertyChanged(); }
    }

    public bool IsDarkTheme
    {
        get => _isDarkTheme;
        set { _isDarkTheme = value; OnPropertyChanged(); RenderSong(); }
    }

    public int FontSize
    {
        get => _fontSize;
        set
        {
            if (value is >= 10 and <= 32)
            {
                _fontSize = value;
                OnPropertyChanged();
                RenderSong();
            }
        }
    }

    public int Transpose
    {
        get => _transpose;
        set { _transpose = value; OnPropertyChanged(); RenderSong(); }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set { _statusMessage = value; OnPropertyChanged(); }
    }

    public bool IsEditing
    {
        get => _isEditing;
        set { _isEditing = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsViewing)); }
    }

    public bool   IsViewing       => !_isEditing;
    public bool   HasSelectedSong => _selectedSong is not null;

    public string EditContent
    {
        get => _editContent;
        set { _editContent = value; OnPropertyChanged(); }
    }

    public string SelectedGenreFilter
    {
        get => _selectedGenreFilter;
        set { _selectedGenreFilter = value; OnPropertyChanged(); FilterSongs(); }
    }

    public SongViewMode ViewMode
    {
        get => _viewMode;
        set
        {
            if (_viewMode == value) return;
            _viewMode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsViewModeText));
            OnPropertyChanged(nameof(IsViewModeTextWithDiagrams));
            OnPropertyChanged(nameof(IsViewModeInlineChordImages));
            SettingsService.Current.ViewMode = value;
            SettingsService.Save();
            RenderSong();
        }
    }

    public bool IsViewModeText
    {
        get => _viewMode == SongViewMode.Text;
        set { if (value) ViewMode = SongViewMode.Text; }
    }

    public bool IsViewModeTextWithDiagrams
    {
        get => _viewMode == SongViewMode.TextWithDiagrams;
        set { if (value) ViewMode = SongViewMode.TextWithDiagrams; }
    }

    public bool IsViewModeInlineChordImages
    {
        get => _viewMode == SongViewMode.InlineChordImages;
        set { if (value) ViewMode = SongViewMode.InlineChordImages; }
    }

    public ICommand NewSongCommand       { get; }
    public ICommand EditSongCommand      { get; }
    public ICommand SaveSongCommand      { get; }
    public ICommand CancelEditCommand    { get; }
    public ICommand DeleteSongCommand    { get; }
    public ICommand FontIncreaseCommand  { get; }
    public ICommand FontDecreaseCommand  { get; }
    public ICommand TransposeUpCommand   { get; }
    public ICommand TransposeDownCommand { get; }
    public ICommand ResetCommand         { get; }
    public ICommand ToggleThemeCommand { get; }
    public ICommand RefreshCommand     { get; }

    public MainViewModel()
    {
        NewSongCommand        = new RelayCommand(_ => NewSong());
        EditSongCommand       = new RelayCommand(_ => StartEdit(),  _ => HasSelectedSong);
        SaveSongCommand       = new RelayCommand(_ => SaveSong(),   _ => IsEditing);
        CancelEditCommand     = new RelayCommand(_ => CancelEdit(), _ => IsEditing);
        DeleteSongCommand     = new RelayCommand(_ => DeleteSong(), _ => HasSelectedSong);
        FontIncreaseCommand   = new RelayCommand(_ => FontSize++, _ => HasSelectedSong && !IsEditing);
        FontDecreaseCommand   = new RelayCommand(_ => FontSize--, _ => HasSelectedSong && !IsEditing);
        TransposeUpCommand    = new RelayCommand(_ => Transpose++, _ => HasSelectedSong && !IsEditing);
        TransposeDownCommand  = new RelayCommand(_ => Transpose--, _ => HasSelectedSong && !IsEditing);
        ResetCommand = new RelayCommand(_ => ResetView(), _ => HasSelectedSong && !IsEditing);  
        ToggleThemeCommand = new RelayCommand(_ => { IsDarkTheme = !IsDarkTheme; ThemeToggleRequested?.Invoke(); });
        RefreshCommand     = new RelayCommand(_ => LoadSongs(), _ => HasSelectedSong && !IsEditing);

        LoadSongs();
        // Bez výběru písně má být BuildWelcomeHtml(); jinak CurrentHtml zůstane "" a WebView je černé
        RenderSong();
    }

    private void LoadSongs()
    {
        AllSongs.Clear();
        foreach (var s in _songService.LoadAllSongs()) AllSongs.Add(s);
        UpdateGenreList();
        FilterSongs();
        StatusMessage = $"Načteno {AllSongs.Count} písní";
    }

    private void ResetView()
    {        
        FontSize     = 16;
        Transpose    = 0;
    }

    private void FilterSongs()
    {
        FilteredSongs.Clear();
        var q = _searchText.ToLowerInvariant().Trim();
        foreach (var song in AllSongs)
        {
            bool matchesSearch = string.IsNullOrEmpty(q)
                || song.Title.ToLowerInvariant().Contains(q)
                || song.Artist.ToLowerInvariant().Contains(q)
                || song.Genre.ToLowerInvariant().Contains(q)
                || song.Key.ToLowerInvariant().Contains(q);

            bool matchesGenre = _selectedGenreFilter is "Vše" || song.Genre == _selectedGenreFilter;

            if (matchesSearch && matchesGenre) FilteredSongs.Add(song);
        }
    }

    private void UpdateGenreList()
    {
        Genres.Clear();
        Genres.Add("Vše");
        foreach (var g in AllSongs.Select(s => s.Genre)
                                  .Where(g => !string.IsNullOrEmpty(g))
                                  .Distinct()
                                  .Order())
            Genres.Add(g);
    }

    private void RenderSong()
    {
        CurrentHtml = _selectedSong switch
        {
            null  => BuildWelcomeHtml(),
            var s => TryRender(s)
        };
    }

    private string TryRender(Song song)
    {
        try   { return _parser.GenerateHtml(song, CurrentSettings()); }
        catch (Exception ex)
              { return $"<html><body style='font-family:sans-serif;padding:20px'><p>Chyba: {ex.Message}</p></body></html>"; }
    }

    private DisplaySettings CurrentSettings()
    {
        var s = _isDarkTheme ? DisplaySettings.DarkTheme() : new DisplaySettings();
        s.FontSize  = _fontSize;
        s.Transpose = _transpose;
        s.ViewMode  = _viewMode;
        return s;
    }

    private void NewSong()
    {
        var song = new Song
        {
            Title  = "Nová píseň",
            Artist = "Interpret",
            ChordProContent = """
                {title: Nová píseň}
                {artist: Interpret}
                {key: C}

                {start_of_verse: Sloka 1}
                [C]Text písně [G]s akordy [Am]zde
                {end_of_verse}

                {start_of_chorus}
                [F]Text refrénu [C]zde
                {end_of_chorus}
                """
        };
        AllSongs.Add(song);
        FilterSongs();
        SelectedSong = song;
        StartEdit();
        StatusMessage = "Nová píseň";
    }

    private void StartEdit()
    {
        if (_selectedSong is null) return;
        EditContent = _selectedSong.ChordProContent;
        IsEditing   = true;
    }

    private void SaveSong()
    {
        if (_selectedSong is null) return;
        _selectedSong.ChordProContent = EditContent;
        SyncMetaFromContent(_selectedSong);
        _songService.SaveSong(_selectedSong);
        IsEditing = false;
        UpdateGenreList();
        FilterSongs();
        RenderSong();
        StatusMessage = $"Uloženo: {_selectedSong.Title}";
    }

    private void CancelEdit()
    {
        IsEditing = false;
        RenderSong();
    }

    private void DeleteSong()
    {
        if (_selectedSong is null) return;
        var name = _selectedSong.Title;
        _songService.DeleteSong(_selectedSong);
        AllSongs.Remove(_selectedSong);
        SelectedSong  = null;
        FilterSongs();
        UpdateGenreList();
        StatusMessage = $"Odstraněno: {name}";
    }

    private static void SyncMetaFromContent(Song song)
    {
        foreach (var line in song.ChordProContent.Split('\n'))
        {
            var m = Regex.Match(line.Trim(), @"^\{([^:}]+)(?::([^}]*))?\}$");
            if (!m.Success) continue;
            var val = m.Groups[2].Value.Trim();
            switch (m.Groups[1].Value.Trim().ToLowerInvariant())
            {
                case "title"  or "t": song.Title  = val; break;
                case "artist" or "a": song.Artist = val; break;
                case "key"    or "k": song.Key    = val; break;
                case "tempo":         song.Tempo  = val; break;
                case "genre":         song.Genre  = val; break;
            }
        }
    }

    private static readonly Lazy<string> _welcomeIconDataUri = new(() =>
    {
        var uri = new Uri("pack://application:,,,/Themes/Resources/ukulele.png");
        using var stream = Application.GetResourceStream(uri)!.Stream;
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return "data:image/png;base64," + Convert.ToBase64String(ms.ToArray());
    });

    private string BuildWelcomeHtml()
    {
        var (bg, fg, accent, sub, hint, hintFg) = _isDarkTheme
            ? ("#1E1E1E", "#E8E8E8", "#64B5F6", "#888888", "#2a2a3a", "#aab4e8")
            : ("#FAFAF8", "#2C2C2C", "#1565C0", "#777777", "#EEF2FF", "#3F51B5");

        return $$"""
            <!DOCTYPE html><html><head><meta charset='UTF-8'>
            <style>
              body { background:{{bg}}; color:{{fg}}; font-family:'Segoe UI',sans-serif;
                     display:flex; align-items:center; justify-content:center;
                     height:100vh; margin:0; text-align:center; }
              .w  { max-width:420px; }
              .ic { width:128px; height:auto; margin:0 auto 16px; display:block; }
              h1  { color:{{accent}}; font-size:28px; margin-bottom:8px; }
              p   { color:{{sub}}; font-size:15px; line-height:1.6; }
              .ht { background:{{hint}}; border-radius:8px; padding:14px;
                    margin-top:20px; color:{{hintFg}}; font-size:13px; }
              code{ background:rgba(0,0,0,.1); padding:1px 5px; border-radius:3px; }
            </style></head><body>
            <div class='w'>
              <img class='ic' src='{{_welcomeIconDataUri.Value}}' alt='Ukulele'>
              <h1>Ukulele Zpěvník</h1>
              <p>Vyberte píseň ze seznamu vlevo nebo přidejte novou pomocí tlačítka <strong>+</strong></p>
              <div class='ht'>
                💡 Texty se zapisují ve formátu <strong>ChordPro</strong><br>
                Akordy do hranatých závorek: <code>[C]</code> <code>[Am]</code> <code>[G7]</code>
              </div>
            </div>
            </body></html>
            """;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
    : ICommand
{
    public bool CanExecute(object? p) => canExecute?.Invoke(p) ?? true;
    public void Execute(object? p)    => execute(p);

    public event EventHandler? CanExecuteChanged
    {
        add    => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }
}
