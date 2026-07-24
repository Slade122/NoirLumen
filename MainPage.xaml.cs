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
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace NativeScreenDimmer_WinUI3;

public sealed partial class MainPage : Page, INotifyPropertyChanged
{
    private readonly DimmerService _dimmerService;
    private readonly SettingsStore _settingsStore = new();
    private readonly RuleEngine _ruleEngine = new();
    private readonly SunCycleService _sunCycleService = new();
    private readonly LocationService _locationService = new();
    private readonly MonitorTopologyService _monitorTopologyService;
    private readonly ProcessRuleWatcher _processRuleWatcher = new();
    private readonly Lock _automationRefreshLock = new();
    private CancellationTokenSource? _automationRefreshCancellationSource;

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
    private bool _suppressAutomationRefresh;
    private bool _isShuttingDown;
    private int _automationRefreshQueued;

    public MainPage()
    {
        _monitorTopologyService = new MonitorTopologyService();
        _dimmerService = new DimmerService(_monitorTopologyService);
        InitializeComponent();
        DataContext = this;
        AppLogger.LogInfo("WinUI MainPage initialized.");

        MonitorSettings.CollectionChanged += MonitorSettings_CollectionChanged;
        Rules.CollectionChanged += Rules_CollectionChanged;
        _processRuleWatcher.ProcessStateChanged += ProcessRuleWatcher_ProcessStateChanged;
        _monitorTopologyService.TopologyChanged += MonitorTopologyService_TopologyChanged;

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

    public int DisplayCount => MonitorSettings.Count;

    public int EnabledDisplayCount => MonitorSettings.Count(setting => setting.IsEnabled);

    public int RuleCount => Rules.Count;

    public int EnabledRuleCount => Rules.Count(rule => rule.IsEnabled);

    public int ProcessRuleCount => Rules.Count(rule => rule.IsEnabled && rule.Type == RuleType.ProcessRunning);

    public string AutomationStateText => AutomationEnabled ? "Automation active" : "Manual mode";

    public string DisplayCountText => $"{DisplayCount} displays";

    public string RuleCountText => $"{RuleCount} rules";

    public string SunStateText => SunMimicEnabled
        ? $"Sun mimic on · cap {SunMimicMaximumDimPercent}%"
        : "Sun mimic off";

    public bool AutomationEnabled
    {
        get => _automationEnabled;
        set
        {
            if (!SetField(ref _automationEnabled, value))
            {
                return;
            }

            NotifyDashboardStateChanged();
            InvalidateAutomationState();
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

            NotifyDashboardStateChanged();
            InvalidateAutomationState();
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

            NotifyDashboardStateChanged();
            InvalidateAutomationState();
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

            InvalidateAutomationState();
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

            InvalidateAutomationState();
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
        set
        {
            if (!SetField(ref _selectedRule, value))
            {
                return;
            }

            NotifyDashboardStateChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void MainPage_Unloaded(object sender, RoutedEventArgs e)
    {
        _isShuttingDown = true;
        CancelPendingAutomationRefresh();
        _processRuleWatcher.Dispose();
        _monitorTopologyService.TopologyChanged -= MonitorTopologyService_TopologyChanged;
        _monitorTopologyService.Dispose();
        SaveSettings();
        _dimmerService.Dispose();
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

        NotifyDashboardStateChanged();
        if (!_suppressAutomationRefresh)
        {
            InvalidateAutomationState();
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

        InvalidateAutomationState();
    }

    private void MonitorSetting_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        NotifyDashboardStateChanged();
        InvalidateAutomationState();
    }

    private void Rule_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        NotifyDashboardStateChanged();
        InvalidateAutomationState();
    }

    private void MonitorTopologyService_TopologyChanged(object? sender, EventArgs e)
    {
        if (_isShuttingDown)
        {
            return;
        }

        RefreshMonitors(MonitorSettings.ToList());
        InvalidateAutomationState();
    }

    private void LoadSettings()
    {
        _suppressAutomationRefresh = true;
        try
        {
            AppSettings loadedSettings = _settingsStore.Load();
            ApplySettings(loadedSettings);
        }
        finally
        {
            _suppressAutomationRefresh = false;
        }

        NotifyDashboardStateChanged();
        RefreshAutomationState();
    }

    private void SaveSettings()
    {
        _settingsStore.Save(BuildCurrentSettings());
    }

    private AppSettings BuildCurrentSettings()
    {
        return new AppSettings
        {
            Monitors = MonitorSettings.Select(setting => setting.Clone()).ToList(),
            Rules = Rules.ToList(),
            AutomationEnabled = AutomationEnabled,
            ThemeMode = AppThemeSelection,
            SunMimicEnabled = SunMimicEnabled,
            Latitude = Latitude,
            Longitude = Longitude,
            SunMimicMaximumDimPercent = SunMimicMaximumDimPercent
        };
    }

    private void ApplySettings(AppSettings loadedSettings)
    {
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

        NotifyDashboardStateChanged();
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
        string modelLabel = string.IsNullOrWhiteSpace(displayArea.ModelName)
            ? $"Monitor {monitorIndex}"
            : displayArea.ModelName;
        return $"{modelLabel} ({primaryLabel}) - {displayArea.Width}x{displayArea.Height} @ {displayArea.Left},{displayArea.Top}";
    }

    private void ApplyEffectiveSettings()
    {
        DateTime now = DateTime.Now;
        IReadOnlySet<string> runningProcessNames = _processRuleWatcher.GetRunningProcessNamesSnapshot();
        UpdateRuleActivityState(now, runningProcessNames);

        List<MonitorDimSetting> mutableSettings = AutomationEnabled
            ? _ruleEngine.BuildEffectiveSettings(MonitorSettings, Rules, now, runningProcessNames)
            : MonitorSettings.Select(setting => setting.Clone()).ToList();

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

    private void UpdateRuleActivityState(DateTime now, IReadOnlySet<string> runningProcessNames)
    {
        foreach (AutoRule rule in Rules)
        {
            bool isRuntimeActive = AutomationEnabled
                && _ruleEngine.IsRuleConditionActive(rule, now, runningProcessNames);
            rule.IsRuntimeActive = isRuntimeActive;
        }
    }

    private void NotifyDashboardStateChanged()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayCount)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EnabledDisplayCount)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RuleCount)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EnabledRuleCount)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ProcessRuleCount)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AutomationStateText)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayCountText)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RuleCountText)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SunStateText)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedRuleSummaryText)));
    }

    private void RefreshAutomationState()
    {
        if (_isShuttingDown)
        {
            return;
        }

        CancelPendingAutomationRefresh();
        RefreshProcessRuleWatcher();
        ApplyEffectiveSettings();
        ScheduleNextAutomationRefresh();
    }

    private void InvalidateAutomationState()
    {
        if (_isShuttingDown || _suppressAutomationRefresh)
        {
            return;
        }

        if (Interlocked.Exchange(ref _automationRefreshQueued, 1) == 1)
        {
            return;
        }

        if (!DispatcherQueue.TryEnqueue(() =>
        {
            Interlocked.Exchange(ref _automationRefreshQueued, 0);
            RefreshAutomationState();
        }))
        {
            Interlocked.Exchange(ref _automationRefreshQueued, 0);
            AppLogger.LogWarning("Automation refresh queue was unavailable.");
        }
    }

    private void RefreshProcessRuleWatcher()
    {
        IReadOnlyList<string> watchedProcessNames = AutomationEnabled
            ? Rules
                .Where(rule => rule.IsEnabled && rule.Type == RuleType.ProcessRunning)
                .Select(rule => NormalizeProcessName(rule.ProcessName))
                .Where(processName => !string.IsNullOrWhiteSpace(processName))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList()
            : [];

        _processRuleWatcher.UpdateWatchedProcessNames(watchedProcessNames);
    }

    private void ProcessRuleWatcher_ProcessStateChanged(object? sender, EventArgs e) => InvalidateAutomationState();

    private void ScheduleNextAutomationRefresh()
    {
        CancelPendingAutomationRefresh();
        DateTime now = DateTime.Now;
        DateTime? nextRefreshTime = ComputeNextAutomationRefreshTime(now);
        if (nextRefreshTime is null)
        {
            return;
        }

        TimeSpan delay = nextRefreshTime.Value - now;
        if (delay < TimeSpan.Zero)
        {
            delay = TimeSpan.Zero;
        }

        CancellationTokenSource cancellationTokenSource = new();
        lock (_automationRefreshLock)
        {
            _automationRefreshCancellationSource = cancellationTokenSource;
        }

        _ = WaitForAutomationRefreshAsync(delay, cancellationTokenSource.Token);
    }

    private async Task WaitForAutomationRefreshAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            InvalidateAutomationState();
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void CancelPendingAutomationRefresh()
    {
        CancellationTokenSource? cancellationTokenSource;
        lock (_automationRefreshLock)
        {
            cancellationTokenSource = _automationRefreshCancellationSource;
            _automationRefreshCancellationSource = null;
        }

        if (cancellationTokenSource is null)
        {
            return;
        }

        cancellationTokenSource.Cancel();
        cancellationTokenSource.Dispose();
    }

    private DateTime? ComputeNextAutomationRefreshTime(DateTime now)
    {
        List<DateTime> candidateRefreshTimes = [];

        if (SunMimicEnabled)
        {
            candidateRefreshTimes.Add(GetNextMinuteBoundary(now));
        }

        if (AutomationEnabled)
        {
            foreach (AutoRule rule in Rules)
            {
                if (!rule.IsEnabled || rule.Type != RuleType.TimeRange)
                {
                    continue;
                }

                DateTime? nextRuleTransition = GetNextTimeRuleTransition(now, rule);
                if (nextRuleTransition is not null)
                {
                    candidateRefreshTimes.Add(nextRuleTransition.Value);
                }
            }
        }

        return candidateRefreshTimes.Count == 0
            ? null
            : candidateRefreshTimes.Min();
    }

    private static DateTime GetNextMinuteBoundary(DateTime now)
    {
        DateTime nextMinute = now.AddMinutes(1);
        return new DateTime(nextMinute.Year, nextMinute.Month, nextMinute.Day, nextMinute.Hour, nextMinute.Minute, 0, now.Kind);
    }

    private static DateTime? GetNextTimeRuleTransition(DateTime now, AutoRule rule)
    {
        if (!TimeOnly.TryParse(rule.StartTime, out TimeOnly startTime))
        {
            return null;
        }

        if (!TimeOnly.TryParse(rule.EndTime, out TimeOnly endTime))
        {
            return null;
        }

        if (startTime == endTime)
        {
            return null;
        }

        DateTime today = now.Date;
        List<DateTime> candidateTransitionTimes =
        [
            today + startTime.ToTimeSpan(),
            today + endTime.ToTimeSpan(),
            today.AddDays(1) + startTime.ToTimeSpan(),
            today.AddDays(1) + endTime.ToTimeSpan()
        ];

        return candidateTransitionTimes
            .Where(candidateTime => candidateTime > now)
            .Min();
    }

    private static string NormalizeProcessName(string processName)
    {
        string normalizedProcessName = System.IO.Path.GetFileNameWithoutExtension(processName.Trim());
        return normalizedProcessName;
    }

    public string SelectedRuleSummaryText => SelectedRule is null
        ? "Select a rule to edit."
        : $"Editing {SelectedRule.Name}.";

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
        foreach (MonitorDimSetting setting in settings)
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

    private void ApplyNow_OnClick(object sender, RoutedEventArgs e)
    {
        RefreshAutomationState();
    }

    private void Save_OnClick(object sender, RoutedEventArgs e) => SaveSettings();

    private void BalancedPreset_OnClick(object sender, RoutedEventArgs e)
    {
        ApplyBalancedPreset();
    }

    private void EveningPreset_OnClick(object sender, RoutedEventArgs e)
    {
        ApplyEveningPreset();
    }

    private void PerformancePreset_OnClick(object sender, RoutedEventArgs e)
    {
        ApplyPerformancePreset();
    }

    private async void ImportSettings_OnClick(object sender, RoutedEventArgs e)
    {
        string? importPath = await PickSettingsFileAsync(false);
        if (importPath is null)
        {
            return;
        }

        try
        {
            AppSettings loadedSettings = _settingsStore.LoadFromPath(importPath);
            ApplySettings(loadedSettings);
            SaveSettings();
        }
        catch (Exception exception)
        {
            AppLogger.LogException("Unable to import settings", exception);
            _ = ShowMessageDialogAsync("Import failed", exception.Message);
        }
    }

    private async void ExportSettings_OnClick(object sender, RoutedEventArgs e)
    {
        string? exportPath = await PickSettingsFileAsync(true);
        if (exportPath is null)
        {
            return;
        }

        try
        {
            _settingsStore.SaveToPath(BuildCurrentSettings(), exportPath);
        }
        catch (Exception exception)
        {
            AppLogger.LogException("Unable to export settings", exception);
            _ = ShowMessageDialogAsync("Export failed", exception.Message);
        }
    }

    private async void AddTimeRule_OnClick(object sender, RoutedEventArgs e)
    {
        AutoRule ruleDraft = new()
        {
            Name = "Night dim",
            Type = RuleType.TimeRange,
            StartTime = "21:00",
            EndTime = "06:00",
            TargetScope = RuleTargetScope.AllScreens,
            DimPercent = 55,
            Priority = 100
        };

        if (!await ShowRuleEditorAsync(ruleDraft))
        {
            return;
        }

        Rules.Add(ruleDraft);
        SelectedRule = ruleDraft;
    }

    private async void AddProcessRule_OnClick(object sender, RoutedEventArgs e)
    {
        AutoRule ruleDraft = new()
        {
            Name = "When app opens",
            Type = RuleType.ProcessRunning,
            ProcessName = "game.exe",
            TargetScope = RuleTargetScope.AllScreens,
            DimPercent = 35,
            Priority = 200
        };

        if (!await ShowRuleEditorAsync(ruleDraft))
        {
            return;
        }

        Rules.Add(ruleDraft);
        SelectedRule = ruleDraft;
    }

    private async void EditRule_OnClick(object sender, RoutedEventArgs e)
    {
        if (SelectedRule is null)
        {
            return;
        }

        AutoRule draftRule = SelectedRule.Clone();
        if (!await ShowRuleEditorAsync(draftRule))
        {
            return;
        }

        SelectedRule.CopyFrom(draftRule);
    }

    private async void DuplicateRule_OnClick(object sender, RoutedEventArgs e)
    {
        if (SelectedRule is null)
        {
            return;
        }

        AutoRule duplicateRule = SelectedRule.Clone();
        duplicateRule.Name = $"{SelectedRule.Name} copy";

        if (!await ShowRuleEditorAsync(duplicateRule))
        {
            return;
        }

        Rules.Add(duplicateRule);
        SelectedRule = duplicateRule;
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

    private async Task<bool> ShowRuleEditorAsync(AutoRule ruleDraft)
    {
        RuleEditorDialog dialog = new(ruleDraft, MonitorSettings)
        {
            XamlRoot = XamlRoot
        };

        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private void ApplyPreset(bool automationEnabled, bool sunMimicEnabled, int maximumSunDimPercent)
    {
        AppLogger.LogInfo($"Applying preset automation={automationEnabled} sunMimic={sunMimicEnabled} maxSunDim={maximumSunDimPercent}.");
        AutomationEnabled = automationEnabled;
        SunMimicEnabled = sunMimicEnabled;
        SunMimicMaximumDimPercent = maximumSunDimPercent;
        SaveSettings();
    }

    public void ApplyBalancedPreset()
    {
        ApplyPreset(true, true, 55);
    }

    public void ApplyEveningPreset()
    {
        ApplyPreset(true, true, 70);
    }

    public void ApplyPerformancePreset()
    {
        ApplyPreset(false, false, 0);
    }

    public void SetAutomationEnabledFromTray(bool automationEnabled)
    {
        AppLogger.LogInfo($"Tray automation toggle set to {automationEnabled}.");
        AutomationEnabled = automationEnabled;
        SaveSettings();
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

    private async Task<string?> PickSettingsFileAsync(bool isSaveDialog)
    {
        MainWindow? mainWindow = ((App)Application.Current).MainWindowInstance;
        if (mainWindow is null)
        {
            return null;
        }

        IntPtr windowHandle = WindowNative.GetWindowHandle(mainWindow);
        if (isSaveDialog)
        {
            FileSavePicker savePicker = new()
            {
                SuggestedFileName = "NoirLumen-settings",
                CommitButtonText = "Export"
            };
            savePicker.FileTypeChoices.Add("JSON file", ["*.json"]);
            InitializeWithWindow.Initialize(savePicker, windowHandle);

            StorageFile? selectedFile = await savePicker.PickSaveFileAsync();
            return selectedFile?.Path;
        }

        FileOpenPicker openPicker = new();
        openPicker.FileTypeFilter.Add(".json");
        InitializeWithWindow.Initialize(openPicker, windowHandle);

        StorageFile? importedFile = await openPicker.PickSingleFileAsync();
        return importedFile?.Path;
    }

    private async Task ShowMessageDialogAsync(string title, string message)
    {
        ContentDialog dialog = new()
        {
            Title = title,
            CloseButtonText = "Close",
            Content = new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap
            },
            XamlRoot = XamlRoot
        };

        await dialog.ShowAsync();
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
