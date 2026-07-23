using System.Text.Json;
using NativeScreenDimmer_WinUI3.Models;

namespace NativeScreenDimmer_WinUI3.Services;

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _settingsPath;

    public SettingsStore()
    {
        string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string settingsDirectoryPath = Path.Combine(appDataPath, "NativeScreenDimmer");
        _settingsPath = Path.Combine(settingsDirectoryPath, "settings.json");
    }

    public AppSettings Load()
    {
        using IDisposable operation = AppLogger.BeginOperation(nameof(Load));
        if (!File.Exists(_settingsPath))
        {
            AppLogger.LogInfo($"Settings file not found at '{_settingsPath}'. Using defaults.");
            return new AppSettings();
        }

        string json = File.ReadAllText(_settingsPath);
        AppSettings? settings = JsonSerializer.Deserialize<AppSettings>(json, SerializerOptions);
        AppLogger.LogInfo($"Loaded settings from '{_settingsPath}'.");
        return settings ?? new AppSettings();
    }

    public void Save(AppSettings settings)
    {
        using IDisposable operation = AppLogger.BeginOperation(nameof(Save));
        SaveToPath(settings, _settingsPath);
    }

    public AppSettings LoadFromPath(string settingsPath)
    {
        using IDisposable operation = AppLogger.BeginOperation(nameof(LoadFromPath));
        if (!File.Exists(settingsPath))
        {
            throw new FileNotFoundException($"Settings file not found at '{settingsPath}'.", settingsPath);
        }

        string json = File.ReadAllText(settingsPath);
        AppSettings? settings = JsonSerializer.Deserialize<AppSettings>(json, SerializerOptions);
        AppLogger.LogInfo($"Loaded settings from '{settingsPath}'.");
        return settings ?? new AppSettings();
    }

    public void SaveToPath(AppSettings settings, string settingsPath)
    {
        using IDisposable operation = AppLogger.BeginOperation(nameof(SaveToPath));
        string? directoryPath = Path.GetDirectoryName(settingsPath);
        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            throw new InvalidOperationException($"Cannot resolve directory for settings file '{settingsPath}'.");
        }

        Directory.CreateDirectory(directoryPath);
        string json = JsonSerializer.Serialize(settings, SerializerOptions);
        File.WriteAllText(settingsPath, json);
        AppLogger.LogInfo($"Saved settings to '{settingsPath}'.");
    }

    public string GetDefaultSettingsPath() => _settingsPath;
}

public sealed class AppSettings
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
