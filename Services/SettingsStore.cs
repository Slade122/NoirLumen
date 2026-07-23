using System.Text.Json;
using NativeScreenDimmer_WinUI3.Models;

namespace NativeScreenDimmer_WinUI3.Services;

internal sealed class SettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _settingsPath;

    public SettingsStore()
    {
        string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string settingsDirectoryPath = System.IO.Path.Combine(appDataPath, "NativeScreenDimmer");
        _settingsPath = System.IO.Path.Combine(settingsDirectoryPath, "settings.json");
    }

    public AppSettings Load()
    {
        using IDisposable operation = AppLogger.BeginOperation(nameof(Load));
        if (!System.IO.File.Exists(_settingsPath))
        {
            AppLogger.LogInfo($"Settings file not found at '{_settingsPath}'. Using defaults.");
            return new AppSettings();
        }

        string json = System.IO.File.ReadAllText(_settingsPath);
        AppSettings? settings = JsonSerializer.Deserialize<AppSettings>(json, SerializerOptions);
        AppLogger.LogInfo($"Loaded settings from '{_settingsPath}'.");
        return settings ?? new AppSettings();
    }

    public void Save(AppSettings settings)
    {
        using IDisposable operation = AppLogger.BeginOperation(nameof(Save));
        string? directoryPath = System.IO.Path.GetDirectoryName(_settingsPath);
        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            throw new InvalidOperationException($"Cannot resolve directory for settings file '{_settingsPath}'.");
        }

        System.IO.Directory.CreateDirectory(directoryPath);
        string json = JsonSerializer.Serialize(settings, SerializerOptions);
        System.IO.File.WriteAllText(_settingsPath, json);
        AppLogger.LogInfo($"Saved settings to '{_settingsPath}'.");
    }
}

internal sealed class AppSettings
{
    public List<MonitorDimSetting> Monitors { get; set; } = [];

    public List<AutoRule> Rules { get; set; } = [];

    public bool AutomationEnabled { get; set; }

    public AppThemeMode ThemeMode { get; set; } = AppThemeMode.Dark;

    public bool SunMimicEnabled { get; set; }

    public double Latitude { get; set; } = 40.7128;

    public double Longitude { get; set; } = -74.0060;

    public int SunMimicMaximumDimPercent { get; set; } = 55;
}

