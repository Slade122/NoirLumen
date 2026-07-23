using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using NativeScreenDimmer_WinUI3.Models;
using NativeScreenDimmer_WinUI3.Services;

namespace NativeScreenDimmer_WinUI3;

public sealed partial class MainPage : Page, INotifyPropertyChanged
{
    private readonly DimmerService _dimmerService = new();
    private readonly SettingsStore _settingsStore = new();
    private readonly RuleEngine _ruleEngine = new();
    private readonly SunCycleService _sunCycleService = new();
    private readonly LocationService _locationService = new();
    private readonly MonitorTopologyService _monitorTopologyService = new();
    private readonly DispatcherTimer _automationTimer;

    private bool _automationEnabled;
    private bool _sunMimicEnabled;
    private int _sunMimicMaximumDimPercent = 55;
    private double _latitude = 40.7128;
    private double _longitude = -74.0060;
    private string _sunMimicStatusText = "Sun mimic inactive";
    private AppThemeMode _appThemeSelection = AppThemeMode.Dark;
    private AutoRule? _selectedRule;
    private int _lastAppliedSignatureHash;
    private bool _hasAppliedSignatureHash;

    public MainPage()
    {
        InitializeComponent();
        DataContext = this;
        AppLogger.LogInfo("WinUI MainPage initialized.");

        MonitorSettings.CollectionChanged += MonitorSettings_CollectionChanged;
        Rules.CollectionChanged += Rules_CollectionChanged;

        _automationTimer = new DispatcherTimer();
        _automationTimer.Interval = TimeSpan.FromMilliseconds(1000);
        _automationTimer.Tick += AutomationTimer_Tick;
        _automationTimer.Start();

        RefreshMonitors([]);
        LoadSettings();
        Unloaded += MainPage_Unloaded;
    }

    public ObservableCollection<MonitorDimSetting> MonitorSettings { get; } = [];

    public ObservableCollection<AutoRule> Rules { get; } = [];

    public AppThemeMode[] ThemeModes { get; } = Enum.GetValues<AppThemeMode>();

    public BlueLightMode[] BlueLightModes { get; } = Enum.GetValues<BlueLightMode>();

    public RuleType[] RuleTypes { get; } = Enum.GetValues<RuleType>();

    public RuleTargetScope[] RuleTargetScopes { get; } = Enum.GetValues<RuleTargetScope>();

    public bool AutomationEnabled
    {
        get => _automationEnabled;
        set
        {
            if (!SetField(ref _automationEnabled, value))
            {
                return;
            }

            ApplyEffectiveSettings();
        }
    }

    public bool SunMimicEnabled
    {
        get => _sunMimicEnabled;
        set
        {
            if (!SetField(ref _sunMimicEnabled, value))
            {
                return;
            }

            ApplyEffectiveSettings();
        }
    }

    public int SunMimicMaximumDimPercent
    {
        get => _sunMimicMaximumDimPercent;
        set
        {
            int clampedValue = Math.Clamp(value, 0, 95);
            if (!SetField(ref _sunMimicMaximumDimPercent, clampedValue))
            {
                return;
            }

            ApplyEffectiveSettings();
        }
    }

    public double Latitude
    {
        get => _latitude;
        set
        {
            if (!SetField(ref _latitude, value))
            {
                return;
            }

            ApplyEffectiveSettings();
        }
    }

    public double Longitude
    {
        get => _longitude;
        set
        {
            if (!SetField(ref _longitude, value))
            {
                return;
            }

            ApplyEffectiveSettings();
        }
    }

    public string SunMimicStatusText
    {
        get => _sunMimicStatusText;
        private set => SetField(ref _sunMimicStatusText, value);
    }

    public AppThemeMode AppThemeSelection
    {
        get => _appThemeSelection;
        set
        {
            if (!SetField(ref _appThemeSelection, value))
            {
                return;
            }

            RequestedTheme = value == AppThemeMode.Dark ? ElementTheme.Dark : ElementTheme.Light;
        }
    }

    public AutoRule? SelectedRule
    {
        get => _selectedRule;
        set => SetField(ref _selectedRule, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void MainPage_Unloaded(object sender, RoutedEventArgs e)
    {
        _automationTimer.Stop();
        SaveSettings();
        _dimmerService.Dispose();
    }

    private void AutomationTimer_Tick(object? sender, object e)
    {
        if (!SunMimicEnabled && !AutomationEnabled)
        {
            return;
        }

        ApplyEffectiveSettings();
    }

    private void MonitorSettings_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (MonitorDimSetting oldSetting in e.OldItems)
            {
                oldSetting.PropertyChanged -= MonitorSetting_PropertyChanged;
            }
        }

        if (e.NewItems is not null)
        {
            foreach (MonitorDimSetting newSetting in e.NewItems)
            {
                newSetting.PropertyChanged += MonitorSetting_PropertyChanged;
            }
        }
    }

    private void Rules_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (AutoRule oldRule in e.OldItems)
            {
                oldRule.PropertyChanged -= Rule_PropertyChanged;
            }
        }

        if (e.NewItems is not null)
        {
            foreach (AutoRule newRule in e.NewItems)
            {
                newRule.PropertyChanged += Rule_PropertyChanged;
            }
        }

        ApplyEffectiveSettings();
    }

    private void MonitorSetting_PropertyChanged(object? sender, PropertyChangedEventArgs e) => ApplyEffectiveSettings();

    private void Rule_PropertyChanged(object? sender, PropertyChangedEventArgs e) => ApplyEffectiveSettings();

    private void LoadSettings()
    {
        AppSettings loadedSettings = _settingsStore.Load();
        RefreshMonitors(loadedSettings.Monitors);

        Rules.Clear();
        foreach (AutoRule rule in loadedSettings.Rules)
        {
            Rules.Add(rule);
        }

        AppThemeSelection = loadedSettings.ThemeMode;
        SunMimicEnabled = loadedSettings.SunMimicEnabled;
        SunMimicMaximumDimPercent = loadedSettings.SunMimicMaximumDimPercent;
        Latitude = loadedSettings.Latitude;
        Longitude = loadedSettings.Longitude;
        AutomationEnabled = loadedSettings.AutomationEnabled;
        ApplyEffectiveSettings();
    }

    private void SaveSettings()
    {
        _settingsStore.Save(new AppSettings
        {
            Monitors = MonitorSettings.Select(setting => setting.Clone()).ToList(),
            Rules = Rules.ToList(),
            AutomationEnabled = AutomationEnabled,
            ThemeMode = AppThemeSelection,
            SunMimicEnabled = SunMimicEnabled,
            Latitude = Latitude,
            Longitude = Longitude,
            SunMimicMaximumDimPercent = SunMimicMaximumDimPercent
        });
    }

    private void RefreshMonitors(IReadOnlyList<MonitorDimSetting> existingSettings)
    {
        Dictionary<string, MonitorDimSetting> existingByDevice = existingSettings.ToDictionary(
            setting => setting.DeviceName,
            StringComparer.OrdinalIgnoreCase);

        IReadOnlyList<MonitorDescriptor> displayAreas = _monitorTopologyService.GetMonitors();
        List<MonitorDimSetting> mergedSettings = [];
        for (int monitorIndex = 0; monitorIndex < displayAreas.Count; monitorIndex++)
        {
            MonitorDescriptor displayArea = displayAreas[monitorIndex];
            string deviceKey = BuildDeviceKey(displayArea, monitorIndex + 1);
            if (existingByDevice.TryGetValue(deviceKey, out MonitorDimSetting? existing))
            {
                existing.DisplayName = BuildDisplayName(displayArea, monitorIndex + 1);
                mergedSettings.Add(existing);
                continue;
            }

            mergedSettings.Add(new MonitorDimSetting
            {
                DeviceName = deviceKey,
                DisplayName = BuildDisplayName(displayArea, monitorIndex + 1),
                IsEnabled = false,
                DimPercent = 0,
                ColorHex = "#000000",
                BlueLightMode = BlueLightMode.Off,
                SunMimicEnabled = true
            });
        }

        MonitorSettings.Clear();
        foreach (MonitorDimSetting setting in mergedSettings)
        {
            MonitorSettings.Add(setting);
        }
    }

    private static string BuildDeviceKey(MonitorDescriptor displayArea, int monitorIndex)
    {
        return string.IsNullOrWhiteSpace(displayArea.DeviceName)
            ? $"{monitorIndex}:{displayArea.Left}:{displayArea.Top}:{displayArea.Width}:{displayArea.Height}"
            : displayArea.DeviceName;
    }

    private static string BuildDisplayName(MonitorDescriptor displayArea, int monitorIndex)
    {
        string primaryLabel = displayArea.IsPrimary ? "Primary" : "Secondary";
        return $"Monitor {monitorIndex} ({primaryLabel}) - {displayArea.Width}x{displayArea.Height} @ {displayArea.Left},{displayArea.Top}";
    }

    private void ApplyEffectiveSettings()
    {
        IReadOnlyList<MonitorDimSetting> effectiveSettings = AutomationEnabled
            ? _ruleEngine.BuildEffectiveSettings(MonitorSettings, Rules, DateTime.Now)
            : MonitorSettings.Select(setting => setting.Clone()).ToList();

        List<MonitorDimSetting> mutableSettings = effectiveSettings.Select(setting => setting.Clone()).ToList();
        ApplyBlueLightModes(mutableSettings);
        ApplySunMimic(mutableSettings);

        int stateHash = BuildStateHash(mutableSettings);
        if (_hasAppliedSignatureHash && stateHash == _lastAppliedSignatureHash)
        {
            return;
        }

        _lastAppliedSignatureHash = stateHash;
        _hasAppliedSignatureHash = true;
        _dimmerService.Apply(mutableSettings);
    }

    private void ApplyBlueLightModes(IReadOnlyList<MonitorDimSetting> settings)
    {
        foreach (MonitorDimSetting setting in settings)
        {
            (string colorHex, int minimumDimPercent) = BlueLightService.ApplyMode(setting.ColorHex, setting.BlueLightMode);
            setting.ColorHex = colorHex;

            if (setting.BlueLightMode == BlueLightMode.Off)
            {
                continue;
            }

            setting.IsEnabled = true;
            setting.DimPercent = Math.Max(setting.DimPercent, minimumDimPercent);
        }
    }

    private void ApplySunMimic(IReadOnlyList<MonitorDimSetting> settings)
    {
        if (!SunMimicEnabled)
        {
            SunMimicStatusText = "Sun mimic inactive";
            return;
        }

        int dimPercent = _sunCycleService.CalculateSunMimicDimPercent(DateTime.Now, Latitude, Longitude, SunMimicMaximumDimPercent);
        SunMimicStatusText = $"Sun dim: {dimPercent}%";

        if (dimPercent <= 0)
        {
            return;
        }

        foreach (MonitorDimSetting setting in settings)
        {
            if (!setting.SunMimicEnabled)
            {
                continue;
            }

            setting.IsEnabled = true;
            setting.DimPercent = Math.Max(setting.DimPercent, dimPercent);
        }
    }

    private static int BuildStateHash(IReadOnlyList<MonitorDimSetting> settings)
    {
        HashCode hashCode = new();
        foreach (MonitorDimSetting setting in settings.OrderBy(setting => setting.DeviceName, StringComparer.OrdinalIgnoreCase))
        {
            hashCode.Add(setting.DeviceName, StringComparer.OrdinalIgnoreCase);
            hashCode.Add(setting.IsEnabled);
            hashCode.Add(setting.DimPercent);
            hashCode.Add(setting.ColorHex, StringComparer.OrdinalIgnoreCase);
            hashCode.Add((int)setting.BlueLightMode);
            hashCode.Add(setting.SunMimicEnabled);
        }

        return hashCode.ToHashCode();
    }

    private void ApplyNow_OnClick(object sender, RoutedEventArgs e) => ApplyEffectiveSettings();

    private void Save_OnClick(object sender, RoutedEventArgs e) => SaveSettings();

    private void AddTimeRule_OnClick(object sender, RoutedEventArgs e)
    {
        Rules.Add(new AutoRule
        {
            Name = "Night schedule",
            Type = RuleType.TimeRange,
            StartTime = "21:00",
            EndTime = "06:00",
            TargetScope = RuleTargetScope.AllScreens,
            DimPercent = 55,
            Priority = 100
        });
    }

    private void AddProcessRule_OnClick(object sender, RoutedEventArgs e)
    {
        Rules.Add(new AutoRule
        {
            Name = "When app runs",
            Type = RuleType.ProcessRunning,
            ProcessName = "game.exe",
            TargetScope = RuleTargetScope.AllScreens,
            DimPercent = 35,
            Priority = 200
        });
    }

    private void RemoveRule_OnClick(object sender, RoutedEventArgs e)
    {
        if (SelectedRule is null)
        {
            return;
        }

        Rules.Remove(SelectedRule);
    }

    private async void AutoDetectLocation_OnClick(object sender, RoutedEventArgs e)
    {
        SunMimicStatusText = "Detecting location...";
        (double Latitude, double Longitude)? coordinates = await _locationService.TryGetCurrentCoordinatesAsync(TimeSpan.FromSeconds(8));
        if (coordinates is null)
        {
            SunMimicStatusText = "Location unavailable (see log)";
            return;
        }

        Latitude = Math.Round(coordinates.Value.Latitude, 4);
        Longitude = Math.Round(coordinates.Value.Longitude, 4);
        SunMimicStatusText = $"Location set: {Latitude:F4}, {Longitude:F4}";
    }

    private async void PickMonitorColor_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: MonitorDimSetting setting })
        {
            return;
        }

        string? selectedColor = await ShowColorPickerDialogAsync(setting.ColorHex);
        if (selectedColor is null)
        {
            return;
        }

        setting.ColorHex = selectedColor;
    }

    private async void PickRuleColor_OnClick(object sender, RoutedEventArgs e)
    {
        AutoRule? targetRule = sender is Button { Tag: AutoRule ruleFromButton }
            ? ruleFromButton
            : SelectedRule;

        if (targetRule is null)
        {
            return;
        }

        string? selectedColor = await ShowColorPickerDialogAsync(targetRule.ColorHex);
        if (selectedColor is null)
        {
            return;
        }

        targetRule.ColorHex = selectedColor;
    }

    private async Task<string?> ShowColorPickerDialogAsync(string initialColorHex)
    {
        ColorPicker colorPicker = new()
        {
            IsAlphaEnabled = false,
            IsColorSpectrumVisible = true,
            IsHexInputVisible = true,
            IsMoreButtonVisible = false,
            IsColorChannelTextInputVisible = true,
            Color = ParseColor(initialColorHex)
        };

        ContentDialog dialog = new()
        {
            Title = "Choose color",
            PrimaryButtonText = "Apply",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            Content = colorPicker,
            XamlRoot = XamlRoot
        };

        ContentDialogResult result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary)
        {
            return null;
        }

        return $"#{colorPicker.Color.R:X2}{colorPicker.Color.G:X2}{colorPicker.Color.B:X2}";
    }

    private static Windows.UI.Color ParseColor(string colorHex)
    {
        if (!string.IsNullOrWhiteSpace(colorHex))
        {
            string trimmed = colorHex.Trim();
            if (trimmed.StartsWith('#') && trimmed.Length == 7 && trimmed[1..].All(Uri.IsHexDigit))
            {
                byte red = byte.Parse(trimmed[1..3], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                byte green = byte.Parse(trimmed[3..5], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                byte blue = byte.Parse(trimmed[5..7], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                return Windows.UI.Color.FromArgb(0xFF, red, green, blue);
            }
        }

        return Windows.UI.Color.FromArgb(0xFF, 0x00, 0x00, 0x00);
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}
