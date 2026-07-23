using NativeScreenDimmer_WinUI3.Models;
using NativeScreenDimmer_WinUI3.Services;

namespace NativeScreenDimmer.WinUI3.Tests;

public sealed class SettingsStoreTests
{
    [Fact]
    public void SaveAndLoadRoundTrip()
    {
        SettingsStore settingsStore = new();
        string tempPath = Path.Combine(Path.GetTempPath(), $"NoirLumen-{Guid.NewGuid():N}.json");

        try
        {
            AppSettings settings = new()
            {
                AutomationEnabled = true,
                ThemeMode = AppThemeMode.Dark,
                SunMimicEnabled = true,
                Latitude = 33.0734,
                Longitude = -97.3093,
                SunMimicMaximumDimPercent = 70,
                Rules =
                [
                    new AutoRule
                    {
                        Name = "RoundTrip",
                        Type = RuleType.TimeRange,
                        StartTime = "21:00",
                        EndTime = "06:00",
                        DimPercent = 55,
                        Priority = 100
                    }
                ]
            };

            settingsStore.SaveToPath(settings, tempPath);
            AppSettings loadedSettings = settingsStore.LoadFromPath(tempPath);

            Assert.True(loadedSettings.AutomationEnabled);
            Assert.Single(loadedSettings.Rules);
            Assert.Equal("RoundTrip", loadedSettings.Rules[0].Name);
            Assert.Equal(70, loadedSettings.SunMimicMaximumDimPercent);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }
}
