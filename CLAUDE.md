# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Ukebook is a WPF desktop application for managing and displaying ukulele songs in ChordPro format. Written in C# 14 targeting .NET 9.0 (Windows). The UI is in Czech.

## Build & Run

```bash
dotnet restore
dotnet build
dotnet run
```

Or open `Ukebook.sln` in Visual Studio 2022 and press F5. There are no tests.

**Requirements:** .NET 9.0 SDK, Microsoft Edge WebView2 Runtime, Windows 10+.

## Architecture

MVVM pattern with a single ViewModel:

- **Models/Song.cs** — Core data model (Title, Artist, Genre, Key, Capo, Tempo, ChordProContent)
- **ViewModels/MainViewModel.cs** — All application state, commands, filtering, and rendering logic. Implements `INotifyPropertyChanged` with `RelayCommand` for bindings.
- **Views/MainWindow.xaml(.cs)** — Main UI with 3-pane layout (sidebar | splitter | content). Uses two WebView2 instances (display + editor preview) sharing a `CoreWebView2Environment`. Live preview has 600ms debounce.
- **Services/ChordProParser.cs** — Regex-based parser (using `[GeneratedRegex]`) that converts ChordPro text to styled HTML via StringBuilder. Handles chord transposition (modulo 12 semitones), section coloring (verse=blue, chorus=red, bridge=green, tab=monospace), and theme-aware rendering.
- **Services/SongService.cs** — Persistence layer storing a JSON index (`songs_index.json` without ChordProContent) + individual `.cho` files per song in `%APPDATA%\Ukebook\`. Includes lazy-loaded sample songs.
- **Services/ThemeService.cs** — Static service that hot-swaps Light/DarkTheme.xaml in WPF's merged resource dictionaries.
- **Themes/** — Fluent Design-inspired XAML resource dictionaries (LightTheme, DarkTheme, MainTheme, MenuItemAeroOverrides).

## Key Design Decisions

- All HTML is generated inline (no external CSS/HTML files) with proper HTML escaping.
- Song index is kept small by excluding ChordProContent; content lives in separate `.cho` files.
- WebView2 environment is shared and initialized once; rendering is deferred until WebView2 is ready.
- Theme switching replaces resource dictionaries dynamically without re-initializing the UI.
- Filtering works across Title, Artist, Genre, and Key fields simultaneously.

## ChordPro Format

Songs use standard ChordPro: `[C]lyrics` for inline chords, `{directive: value}` for metadata (`title`, `artist`, `key`, `tempo`, `capo`, `genre`), and section markers (`start_of_verse`, `start_of_chorus`, `start_of_bridge`, `start_of_tab` with corresponding `end_of_*`).
