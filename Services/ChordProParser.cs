using System.Collections.Concurrent;
using Ukebook.Models;

namespace Ukebook.Services;

public sealed partial class ChordProParser
{
    [GeneratedRegex(@"\[([^\]]+)\]", RegexOptions.Compiled)]
    private static partial Regex ChordPattern();

    [GeneratedRegex(@"^\{([^:}]+)(?::([^}]*))?\}$", RegexOptions.Compiled)]
    private static partial Regex DirectivePattern();

    private static readonly string ChordImagesPath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory,
        "Themes", "Resources", "chords", "112x150");

    // Cache: chord name → base64 data URI (nebo null, když obrázek neexistuje).
    // Bez cache by se PNG četl při každém překreslení (změna fontu, transpozice…).
    private static readonly ConcurrentDictionary<string, string?> ChordImageCache = new();

    public string GenerateHtml(Song song, DisplaySettings s)
    {
        var lines = song.ChordProContent.Split('\n');
        var meta  = ExtractMeta(lines);
        var sb    = new StringBuilder(4096);

        sb.Append(BuildHtmlHeader(s));
        sb.Append("<div class=\"song-container\">");
        AppendSongHeader(sb, song, meta, s);
        AppendSongBody(sb, lines, s);
        if (s.ViewMode == SongViewMode.TextWithDiagrams) AppendChordDiagrams(sb, lines, s);
        sb.Append("</div></body></html>");

        return sb.ToString();
    }

    private static void AppendChordDiagrams(StringBuilder sb, string[] lines, DisplaySettings s)
    {
        var chords = ExtractUniqueChords(lines, s.Transpose);
        if (chords.Count == 0) return;

        sb.Append("<div class=\"chord-diagrams\">");
        sb.Append("<h3>Použité akordy</h3>");
        sb.Append("<div class=\"chord-grid\">");
        foreach (var chord in chords)
        {
            var dataUri = LoadChordImageDataUri(chord);
            if (dataUri is null) continue;
            sb.Append("<div class=\"chord-item\">");
            sb.Append($"<img src=\"{dataUri}\" alt=\"{Esc(chord)}\">");
            sb.Append($"<span>{Esc(chord)}</span>");
            sb.Append("</div>");
        }
        sb.Append("</div></div>");
    }

    private static List<string> ExtractUniqueChords(string[] lines, int transpose)
    {
        HashSet<string> seen = new(StringComparer.Ordinal);
        List<string> result = [];
        foreach (var line in lines)
        {
            if (line.TrimStart().StartsWith('{')) continue;
            foreach (Match m in ChordPattern().Matches(line))
            {
                var chord = Transpose(m.Groups[1].Value, transpose);
                if (seen.Add(chord)) result.Add(chord);
            }
        }
        return result;
    }

    private static string? LoadChordImageDataUri(string chord) =>
        ChordImageCache.GetOrAdd(chord, name =>
        {
            var path = Path.Combine(ChordImagesPath, $"{name}.png");
            if (!File.Exists(path)) return null;
            return "data:image/png;base64," + Convert.ToBase64String(File.ReadAllBytes(path));
        });

    private static void AppendSongHeader(StringBuilder sb, Song song, SongMeta meta, DisplaySettings s)
    {
        sb.Append("<div class=\"song-header\">");
        sb.Append($"<h1 class=\"song-title\">{Esc(meta.Title ?? song.Title)}</h1>");

        var artist = meta.Artist ?? song.Artist;
        if (!string.IsNullOrEmpty(artist))
            sb.Append($"<div class=\"song-artist\">{Esc(artist)}</div>");

        List<string> items = [];
        var key   = meta.Key   ?? song.Key;
        var genre = meta.Genre ?? song.Genre;

        if (!string.IsNullOrEmpty(key))        items.Add($"<span class=\"meta-item\"><span class=\"meta-label\">Tónina:</span> {Esc(key)}</span>");
        if (!string.IsNullOrEmpty(meta.Tempo)) items.Add($"<span class=\"meta-item\"><span class=\"meta-label\">Tempo:</span> {Esc(meta.Tempo)}</span>");
        if (song.Capo > 0)                     items.Add($"<span class=\"meta-item\"><span class=\"meta-label\">Kapo:</span> {song.Capo}</span>");
        if (!string.IsNullOrEmpty(genre))      items.Add($"<span class=\"meta-item\"><span class=\"meta-label\">Žánr:</span> {Esc(genre)}</span>");

        if (items.Count > 0)
            sb.Append($"<div class=\"song-meta\">{string.Join(" · ", items)}</div>");

        sb.Append("</div>");
    }

    private static void AppendSongBody(StringBuilder sb, string[] lines, DisplaySettings s)
    {
        sb.Append("<div class=\"song-body\">");
        bool inChorus = false, inVerse = false, inBridge = false, inTab = false;

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd();
            var dm   = DirectivePattern().Match(line.Trim());

            if (dm.Success)
            {
                var dKey = dm.Groups[1].Value.Trim().ToLowerInvariant();
                var dVal = dm.Groups[2].Value.Trim();

                switch (dKey)
                {
                    case "title" or "t" or "artist" or "a" or "key" or "k"
                      or "tempo" or "capo" or "genre":
                        break;

                    case "start_of_chorus" or "soc":
                        inChorus = true;
                        sb.Append($"<div class=\"section chorus\"><div class=\"section-label\">{Esc(string.IsNullOrEmpty(dVal) ? "Refrén" : dVal)}</div><div class=\"section-lines\">");
                        break;
                    case "end_of_chorus" or "eoc":
                        inChorus = false;
                        sb.Append("</div></div>");
                        break;

                    case "start_of_verse" or "sov":
                        inVerse = true;
                        sb.Append($"<div class=\"section verse\"><div class=\"section-label\">{Esc(string.IsNullOrEmpty(dVal) ? "Sloka" : dVal)}</div><div class=\"section-lines\">");
                        break;
                    case "end_of_verse" or "eov":
                        inVerse = false;
                        sb.Append("</div></div>");
                        break;

                    case "start_of_bridge" or "sob":
                        inBridge = true;
                        sb.Append($"<div class=\"section bridge\"><div class=\"section-label\">{Esc(string.IsNullOrEmpty(dVal) ? "Bridge" : dVal)}</div><div class=\"section-lines\">");
                        break;
                    case "end_of_bridge" or "eob":
                        inBridge = false;
                        sb.Append("</div></div>");
                        break;

                    case "start_of_tab" or "sot":
                        inTab = true;
                        sb.Append("<div class=\"section tab\"><div class=\"section-label\">Tab</div><pre class=\"tab-content\">");
                        break;
                    case "end_of_tab" or "eot":
                        inTab = false;
                        sb.Append("</pre></div>");
                        break;

                    case "comment" or "c" or "comment_italic" or "ci":
                        sb.Append($"<div class=\"comment\">{Esc(dVal)}</div>");
                        break;

                    case "chorus":
                        sb.Append("<div class=\"chorus-repeat\">↻ Refrén</div>");
                        break;
                }
                continue;
            }

            if (inTab)  { sb.Append(Esc(line)); sb.Append('\n'); continue; }
            if (string.IsNullOrWhiteSpace(line))
            {
                if (!inChorus && !inVerse && !inBridge) sb.Append("<div class=\"line-break\"></div>");
                continue;
            }

            sb.Append(ChordPattern().IsMatch(line)
                ? RenderChordLine(line, s)
                : $"<div class=\"lyric-only\"><span class=\"lyrics\">{Esc(line)}</span></div>");
        }

        sb.Append("</div>");
    }

    private static string RenderChordLine(string line, DisplaySettings s)
    {
        List<(string chord, string lyric)> segments = [];
        int    lastIdx      = 0;
        string pendingChord = "";

        foreach (Match m in ChordPattern().Matches(line))
        {
            var lyricBefore = line[lastIdx..m.Index];
            if (pendingChord.Length > 0 || lyricBefore.Length > 0)
                segments.Add((pendingChord, lyricBefore));
            pendingChord = m.Groups[1].Value;
            lastIdx      = m.Index + m.Length;
        }
        segments.Add((pendingChord, line[lastIdx..]));

        bool inline = s.ViewMode == SongViewMode.InlineChordImages;
        bool hover  = s.ViewMode == SongViewMode.TextWithChordHover;

        var sb = new StringBuilder();
        sb.Append(inline ? "<div class=\"chord-line chord-line-img\">" : "<div class=\"chord-line\">");
        foreach (var (chord, lyric) in segments)
        {
            sb.Append("<span class=\"chord-lyric-pair\">");
            sb.Append(RenderChordCell(chord, inline, hover, s.Transpose));
            sb.Append($"<span class=\"lyrics\">{(string.IsNullOrEmpty(lyric) ? "&nbsp;" : Esc(lyric))}</span>");
            sb.Append("</span>");
        }
        sb.Append("</div>");
        return sb.ToString();
    }

    private static string RenderChordCell(string chord, bool inline, bool hover, int transpose)
    {
        if (string.IsNullOrEmpty(chord))
            return inline ? "<span class=\"chord-img empty\"></span>"
                          : "<span class=\"chord\"></span>";

        var transposed = Transpose(chord, transpose);
        if (!inline)
        {
            if (!hover)
                return $"<span class=\"chord\">{Esc(transposed)}</span>";

            var hoverUri = LoadChordImageDataUri(transposed);
            if (hoverUri is null)
                return $"<span class=\"chord\">{Esc(transposed)}</span>";
            return $"<span class=\"chord chord-hover\">{Esc(transposed)}<span class=\"chord-popup\"><img src=\"{hoverUri}\" alt=\"{Esc(transposed)}\"></span></span>";
        }

        var dataUri = LoadChordImageDataUri(transposed);
        return dataUri is null
            ? $"<span class=\"chord chord-img-fallback\">{Esc(transposed)}</span>"
            : $"<span class=\"chord-img\"><img src=\"{dataUri}\" alt=\"{Esc(transposed)}\"><span class=\"chord-img-label\">{Esc(transposed)}</span></span>";
    }

    private static SongMeta ExtractMeta(string[] lines)
    {
        SongMeta meta = new();
        foreach (var line in lines)
        {
            var m = DirectivePattern().Match(line.Trim());
            if (!m.Success) continue;
            var val = m.Groups[2].Value.Trim();
            switch (m.Groups[1].Value.Trim().ToLowerInvariant())
            {
                case "title"  or "t": meta.Title  = val; break;
                case "artist" or "a": meta.Artist = val; break;
                case "key"    or "k": meta.Key    = val; break;
                case "tempo":         meta.Tempo  = val; break;
                case "genre":         meta.Genre  = val; break;
            }
        }
        return meta;
    }

    private static readonly string[] Sharp = ["C","C#","D","D#","E","F","F#","G","G#","A","A#","B"];
    private static readonly string[] Flat  = ["C","Db","D","Eb","E","F","Gb","G","Ab","A","Bb","B"];

    private static string Transpose(string chord, int semitones)
    {
        if (semitones == 0) return chord;
        foreach (var note in Sharp.Concat(Flat).Distinct().OrderByDescending(n => n.Length))
        {
            if (!chord.StartsWith(note, StringComparison.OrdinalIgnoreCase)) continue;
            int idx = Array.IndexOf(Sharp, note);
            if (idx < 0) idx = Array.IndexOf(Flat, note);
            if (idx < 0) break;
            return Sharp[((idx + semitones) % 12 + 12) % 12] + chord[note.Length..];
        }
        return chord;
    }

    private static string Esc(string? s) =>
        s is null ? string.Empty
                  : s.Replace("&","&amp;").Replace("<","&lt;").Replace(">","&gt;").Replace("\"","&quot;");

    private static string BuildHtmlHeader(DisplaySettings s) => $$"""
        <!DOCTYPE html>
        <html lang="cs"><head><meta charset="UTF-8">
        <style>
          * { box-sizing:border-box; margin:0; padding:0; }
          body { font-family:'{{s.FontFamily}}','Segoe UI',sans-serif; font-size:{{s.FontSize}}px;
                 background:{{s.BackgroundColor}}; color:{{s.TextColor}}; padding:20px 30px; line-height:1.5; }
          .song-container { max-width:900px; margin:0 auto; }
          .song-header { margin-bottom:28px; padding-bottom:16px; border-bottom:2px solid {{s.AccentColor}}; }
          .song-title  { font-size:{{s.FontSize + 10}}px; font-weight:700; color:{{s.AccentColor}}; margin-bottom:4px; }
          .song-artist { font-size:{{s.FontSize + 2}}px; color:{{s.SubtitleColor}}; margin-bottom:8px; }
          .song-meta   { font-size:{{s.FontSize - 2}}px; color:{{s.MetaColor}}; display:flex; flex-wrap:wrap; gap:12px; margin-top:6px; }
          .meta-label  { font-weight:600; }
          .section     { margin-bottom:20px; padding:10px 14px; border-radius:6px; border-left:4px solid transparent; }
          .section-label { font-size:{{s.FontSize - 2}}px; font-weight:700; text-transform:uppercase; letter-spacing:.08em; margin-bottom:8px; opacity:.7; }
          .section.verse  { background:{{s.VerseBg}};  border-left-color:{{s.VerseAccent}};  }
          .section.verse  .section-label { color:{{s.VerseAccent}};  }
          .section.chorus { background:{{s.ChorusBg}}; border-left-color:{{s.ChorusAccent}}; }
          .section.chorus .section-label { color:{{s.ChorusAccent}}; }
          .section.bridge { background:{{s.BridgeBg}}; border-left-color:{{s.BridgeAccent}}; }
          .section.bridge .section-label { color:{{s.BridgeAccent}}; }
          .section.tab    { background:{{s.VerseBg}};  border-left-color:#888; }
          .tab-content    { font-family:'Courier New',monospace; font-size:{{s.FontSize - 1}}px; white-space:pre; }
          .chord-line     { display:flex; flex-wrap:wrap; margin-bottom:2px; align-items:flex-end; min-height:{{s.FontSize * 2 + 6}}px; }
          .chord-lyric-pair { display:inline-flex; flex-direction:column; align-items:flex-start; }
          .chord  { font-weight:700; color:{{s.ChordColor}}; font-size:{{s.FontSize}}px; min-height:{{s.FontSize + 4}}px; line-height:1.2; white-space:nowrap; padding-right:4px; }
          .lyrics { color:{{s.TextColor}}; font-size:{{s.FontSize}}px; white-space:pre; padding-right:2px; }
          .lyric-only    { margin-bottom:2px; }
          .comment       { font-style:italic; color:{{s.MetaColor}}; margin:6px 0; font-size:{{s.FontSize - 1}}px; }
          .chorus-repeat { color:{{s.ChorusAccent}}; font-weight:600; font-style:italic; margin:8px 0; }
          .line-break    { height:12px; }
          .chord-diagrams   { margin-top:36px; padding-top:18px; border-top:2px solid {{s.AccentColor}}; }
          .chord-diagrams h3{ color:{{s.AccentColor}}; margin-bottom:14px; font-size:{{s.FontSize + 2}}px; }
          .chord-grid       { display:flex; flex-wrap:wrap; gap:18px 20px; }
          .chord-item       { display:flex; flex-direction:column; align-items:center; }
          .chord-item img   { width:84px; height:auto; display:block; background:#FFFFFF; padding:4px; border-radius:4px;{{(s.IsDark ? " filter:invert(1);" : "")}} }
          .chord-item span  { margin-top:6px; font-weight:700; color:{{s.ChordColor}}; font-size:{{s.FontSize}}px; }
          .chord-line-img   { min-height:{{s.FontSize * 3 + 28}}px; row-gap:6px; }
          .chord-img        { display:inline-flex; flex-direction:column; align-items:center; padding-right:6px; min-width:{{s.FontSize * 3}}px; }
          .chord-img img    { height:{{s.FontSize * 3}}px; width:auto; display:block; background:#FFFFFF; padding:2px; border-radius:3px;{{(s.IsDark ? " filter:invert(1);" : "")}} }
          .chord-img-label  { font-weight:700; color:{{s.ChordColor}}; font-size:{{s.FontSize - 3}}px; margin-top:2px; }
          .chord-img.empty  { height:{{s.FontSize * 3 + 18}}px; padding-right:0; min-width:0; }
          .chord-img-fallback { font-weight:700; color:{{s.ChordColor}}; font-size:{{s.FontSize}}px; align-self:flex-end; padding-right:6px; }
          .chord-hover      { position:relative; cursor:help; }
          .chord-popup      { display:none; position:absolute; bottom:100%; left:50%; transform:translateX(-50%); margin-bottom:4px; padding:4px; background:#FFFFFF; border-radius:4px; box-shadow:0 2px 10px rgba(0,0,0,.35); z-index:10; pointer-events:none;{{(s.IsDark ? " filter:invert(1);" : "")}} }
          .chord-popup img  { height:{{s.FontSize * 5}}px; width:auto; display:block; }
          .chord-hover:hover .chord-popup { display:block; }
        </style></head><body>
        """;

    private sealed class SongMeta
    {
        public string? Title  { get; set; }
        public string? Artist { get; set; }
        public string? Key    { get; set; }
        public string? Tempo  { get; set; }
        public string? Genre  { get; set; }
    }
}

public sealed class DisplaySettings
{
    public string       FontFamily { get; set; } = "Segoe UI";
    public int          FontSize   { get; set; } = 16;
    public int          Transpose  { get; set; } = 0;
    public SongViewMode ViewMode   { get; set; } = SongViewMode.Text;

    public string BackgroundColor { get; set; } = "#FAFAF8";
    public string TextColor       { get; set; } = "#2C2C2C";
    public string ChordColor      { get; set; } = "#1565C0";
    public string AccentColor     { get; set; } = "#1565C0";
    public string SubtitleColor   { get; set; } = "#555555";
    public string MetaColor       { get; set; } = "#777777";

    public string VerseBg      { get; set; } = "#F0F4FF";
    public string VerseAccent  { get; set; } = "#3F72AF";
    public string ChorusBg     { get; set; } = "#FFF5F0";
    public string ChorusAccent { get; set; } = "#C0392B";
    public string BridgeBg     { get; set; } = "#F0FFF4";
    public string BridgeAccent { get; set; } = "#27AE60";

    public bool IsDark { get; set; }

    public static DisplaySettings DarkTheme() => new()
    {
        IsDark          = true,
        BackgroundColor = "#1E1E1E", TextColor   = "#E8E8E8",
        ChordColor      = "#64B5F6", AccentColor = "#64B5F6",
        SubtitleColor   = "#AAAAAA", MetaColor   = "#888888",
        VerseBg         = "#252535", VerseAccent = "#7986CB",
        ChorusBg        = "#352525", ChorusAccent= "#EF9A9A",
        BridgeBg        = "#253525", BridgeAccent= "#81C784",
    };
}
