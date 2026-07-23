# Configuration

## Settings file

Path:

`%AppData%\NativeScreenDimmer\settings.json`

The file stores:

- monitor list and per-monitor settings
- rules
- automation enabled flag
- theme mode
- sun-mimic enabled flag
- latitude / longitude
- sun-mimic max dim

## Rule types

## TimeRange

- Active between `StartTime` and `EndTime` (supports overnight ranges)

## ProcessRunning

- Active when `ProcessName` (exe basename) is currently running
- Monitored by process start/stop events instead of a fixed polling loop

## Rule targeting

- `AllScreens`
- `SpecificScreen` via `TargetDeviceNames` (semicolon-delimited device keys)

## Color format

- Preferred: `#RRGGBB`
- Named colors are accepted where parsers support them

## Blue-light modes

- `Off` leaves the monitor color unchanged
- `Gentle`, `Warm`, `Amber`, and `Red` use progressively warmer, more natural temperature shifts
- Each mode also enforces a small dim floor so the tint feels like evening light instead of a flat filter

## Sun-mimic tuning

- `SunMimicEnabled`: global toggle
- `SunMimicMaximumDimPercent`: top cap
- per-monitor `SunMimicEnabled`: include/exclude per display

## Logging verbosity

Logging is code-driven today. For production hardening, add a log-level setting if needed.

## Tray controls

The tray menu can apply these runtime settings without opening the main window:

- show/hide window
- preset selection (`Balanced`, `Evening`, `Performance`)
- automation toggle (`On`, `Off`)
