# NoirLumen

NoirLumen is a fast, lightweight, per-monitor screen dimmer for Windows built with WinUI 3 and a native Win32 overlay engine.

## Features

- Per-monitor dimming and tint controls
- Click-through native overlays that work on any monitor (including displays without hardware brightness control)
- Per-monitor blue-light modes (`Off`, `Gentle`, `Warm`, `Amber`, `Red`)
- Sun-mimic mode with per-monitor include/exclude
- Time-based and process-based automation rules
- Auto location detection with provider fallback
- Light/Dark theme support
- Buffered file logging for diagnostics with low runtime overhead

## Requirements

- Windows 10/11 (desktop)
- .NET 10 SDK

## Build

```powershell
dotnet build .\NativeScreenDimmer.WinUI3.csproj
```

## Run

```powershell
dotnet run --project .\NativeScreenDimmer.WinUI3.csproj
```

## Publish

```powershell
dotnet publish .\NativeScreenDimmer.WinUI3.csproj -c Release -r win-x64
```

## Logging

Runtime logs:

`%AppData%\NativeScreenDimmer\logs\app.log`

Saved settings:

`%AppData%\NativeScreenDimmer\settings.json`

## Sun-mimic behavior

- Uses latitude/longitude plus local time
- Dimming follows a sun-power decay profile through late day and into night
- Per-monitor toggle controls whether a monitor participates

## Location detection

Auto-detect uses HTTPS providers with fallback:

1. `ipwho.is`
2. `ipapi.co`
3. `ipinfo.io`

If detection fails, use manual latitude/longitude entry.

## Architecture

See:

- `docs/ARCHITECTURE.md`
- `docs/CONFIGURATION.md`
- `docs/TROUBLESHOOTING.md`

## Performance notes

- 1-second automation tick
- State-hash short-circuit to avoid redundant overlay applies
- Buffered async logging with batched writes
- Single-pass process sampling for process-rule evaluation
