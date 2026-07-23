# Architecture

## High-level

NoirLumen has two layers:

1. **WinUI 3 shell** (`MainWindow`, `MainPage`)
2. **Native dimming/runtime services** (`Services/*`)

The shell also wires a native tray host (`TrayService`) that runs on a dedicated STA thread and dispatches control actions back onto the WinUI thread.

## Core flow

1. UI state is updated from user input, settings load, process events, or scheduled boundary refreshes.
2. `RuleEngine` computes effective monitor settings.
3. `BlueLightService` and sun-mimic processing transform color/dim values.
4. `DimmerService` maps monitor settings to native overlays.
5. `NativeOverlayWindow` applies color + alpha to per-monitor transparent windows.

## Overlay engine

- Built on Win32 (`CreateWindowEx`, `SetLayeredWindowAttributes`, `SetWindowPos`)
- Per-monitor topmost layered windows
- Tool-window + no-activate + transparent style for non-interactive overlays

## Monitor discovery

`MonitorTopologyService` caches `EnumDisplayMonitors` + `GetMonitorInfo` output and invalidates on display/device change events. It emits:

- Device name
- Primary flag
- Coordinates
- Resolution

## Persistence

`SettingsStore` serializes app state to:

`%AppData%\NativeScreenDimmer\settings.json`

## Logging

`AppLogger` is buffered and asynchronous:

- queue-backed batching
- operation timing scopes
- output to `%AppData%\NativeScreenDimmer\logs\app.log`
