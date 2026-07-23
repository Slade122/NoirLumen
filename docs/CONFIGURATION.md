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

## Rule targeting

- `AllScreens`
- `SpecificScreen` via `TargetDeviceName`

## Color format

- Preferred: `#RRGGBB`
- Named colors are accepted where parsers support them

## Sun-mimic tuning

- `SunMimicEnabled`: global toggle
- `SunMimicMaximumDimPercent`: top cap
- per-monitor `SunMimicEnabled`: include/exclude per display

## Logging verbosity

Logging is code-driven today. For production hardening, add a log-level setting if needed.
