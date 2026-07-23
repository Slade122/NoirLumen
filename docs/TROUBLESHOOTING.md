# Troubleshooting

## Auto-detect location shows unavailable

1. Check internet connectivity
2. Verify outbound HTTPS is not blocked
3. Open log file:
   `%AppData%\NativeScreenDimmer\logs\app.log`
4. Use manual latitude/longitude if providers fail

## No dimming appears

1. Ensure monitor card has `Enable overlay` enabled
2. Set `DimPercent` above 0
3. If sun-mimic is enabled, verify current sun profile expects non-zero dim
4. Check log for overlay creation/apply errors

## Dimming applies to wrong monitor

1. Reopen app to refresh topology
2. Confirm monitor arrangement in Windows Display Settings
3. Verify `TargetDeviceName` for specific-screen rules

## Performance concerns

1. Disable unused automation rules
2. Reduce process-based rules if unnecessary
3. Inspect `app.log` for repeated high-cost paths

## Settings not persisting

1. Click `Save`
2. Verify `%AppData%\NativeScreenDimmer\settings.json` exists
3. Confirm no filesystem permission restrictions on `%AppData%`
