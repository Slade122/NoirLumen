using System.ComponentModel;
using System.Globalization;
using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NativeScreenDimmer_WinUI3.Models;
using NativeScreenDimmer_WinUI3.Services;

namespace NativeScreenDimmer_WinUI3;

public sealed partial class RuleEditorDialog : ContentDialog, INotifyPropertyChanged
{
    private readonly AutoRule _ruleDraft;
    private readonly RuleValidationService _ruleValidationService = new();
    private readonly IReadOnlyList<MonitorDimSetting> _monitorSettings;
    private bool _isRefreshingDiagnostics;
    private bool _isRuleValid = true;
    private string _previewText = string.Empty;

    public RuleEditorDialog(AutoRule ruleDraft, IReadOnlyList<MonitorDimSetting> monitorSettings)
    {
        InitializeComponent();
        _ruleDraft = ruleDraft;
        _monitorSettings = monitorSettings;
        DataContext = _ruleDraft;
        RuleTypes = Enum.GetValues<RuleType>();
        RuleTargetScopes = Enum.GetValues<RuleTargetScope>();
        DisplayTargets = BuildDisplayTargets(monitorSettings, _ruleDraft.TargetDeviceName);
        foreach (DisplayTargetOption displayTarget in DisplayTargets)
        {
            displayTarget.PropertyChanged += DisplayTarget_PropertyChanged;
        }

        _ruleDraft.PropertyChanged += RuleDraft_PropertyChanged;
        Closed += RuleEditorDialog_Closed;
        RefreshDiagnostics();
    }

    public AutoRule RuleDraft => _ruleDraft;

    public RuleType[] RuleTypes { get; }

    public RuleTargetScope[] RuleTargetScopes { get; }

    public ObservableCollection<DisplayTargetOption> DisplayTargets { get; }

    public ObservableCollection<string> ValidationIssues { get; } = [];

    public bool IsRuleValid
    {
        get => _isRuleValid;
        private set
        {
            if (_isRuleValid == value)
            {
                return;
            }

            _isRuleValid = value;
            OnPropertyChanged(nameof(IsRuleValid));
            OnPropertyChanged(nameof(ValidationIssuesVisibility));
        }
    }

    public string PreviewText
    {
        get => _previewText;
        private set
        {
            if (_previewText == value)
            {
                return;
            }

            _previewText = value;
            OnPropertyChanged(nameof(PreviewText));
            OnPropertyChanged(nameof(ValidationIssuesVisibility));
        }
    }

    public Visibility ValidationIssuesVisibility => IsRuleValid ? Visibility.Collapsed : Visibility.Visible;

    public Visibility TimeRuleVisibility => _ruleDraft.Type == RuleType.TimeRange ? Visibility.Visible : Visibility.Collapsed;

    public Visibility ProcessRuleVisibility => _ruleDraft.Type == RuleType.ProcessRunning ? Visibility.Visible : Visibility.Collapsed;

    public Visibility SpecificScreenVisibility => _ruleDraft.TargetScope == RuleTargetScope.SpecificScreen ? Visibility.Visible : Visibility.Collapsed;

    public string ConditionHintText => _ruleDraft.Type == RuleType.TimeRange
        ? "Use a 24-hour time like 21:00. The rule wraps across midnight."
        : "Use the exe name, like game.exe. The extension is optional.";

    public event PropertyChangedEventHandler? PropertyChanged;

    private void RuleEditorDialog_Closed(ContentDialog sender, ContentDialogClosedEventArgs args)
    {
        _ruleDraft.PropertyChanged -= RuleDraft_PropertyChanged;
        foreach (DisplayTargetOption displayTarget in DisplayTargets)
        {
            displayTarget.PropertyChanged -= DisplayTarget_PropertyChanged;
        }

        Closed -= RuleEditorDialog_Closed;
    }

    private void RuleDraft_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AutoRule.Type))
        {
            RaiseRuleVisibilityProperties();
        }

        if (e.PropertyName is nameof(AutoRule.TargetScope))
        {
            OnPropertyChanged(nameof(SpecificScreenVisibility));
        }

        if (e.PropertyName is nameof(AutoRule.Type) || e.PropertyName is nameof(AutoRule.TargetScope))
        {
            OnPropertyChanged(nameof(ConditionHintText));
        }

        RefreshDiagnostics();
    }

    private void DisplayTarget_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not nameof(DisplayTargetOption.IsSelected))
        {
            return;
        }

        UpdateRuleTargetDeviceNames();
        RefreshDiagnostics();
    }

    private void RaiseRuleVisibilityProperties()
    {
        OnPropertyChanged(nameof(TimeRuleVisibility));
        OnPropertyChanged(nameof(ProcessRuleVisibility));
    }

    private void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private void RefreshDiagnostics()
    {
        if (_isRefreshingDiagnostics)
        {
            return;
        }

        _isRefreshingDiagnostics = true;
        try
        {
            RuleValidationResult validationResult = _ruleValidationService.Validate(_ruleDraft, _monitorSettings);
            ValidationIssues.Clear();
            foreach (string issue in validationResult.Issues)
            {
                ValidationIssues.Add(issue);
            }

            IsRuleValid = validationResult.IsValid;
            PreviewText = validationResult.PreviewText;
            OnPropertyChanged(nameof(ValidationIssues));
        }
        finally
        {
            _isRefreshingDiagnostics = false;
        }
    }

    private async void PickColor_OnClick(object sender, RoutedEventArgs e)
    {
        ColorPicker colorPicker = new()
        {
            IsAlphaEnabled = false,
            IsColorSpectrumVisible = true,
            IsHexInputVisible = true,
            IsMoreButtonVisible = false,
            IsColorChannelTextInputVisible = true,
            Color = ParseColor(_ruleDraft.ColorHex)
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

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        _ruleDraft.ColorHex = $"#{colorPicker.Color.R:X2}{colorPicker.Color.G:X2}{colorPicker.Color.B:X2}";
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

    private static ObservableCollection<DisplayTargetOption> BuildDisplayTargets(
        IReadOnlyList<MonitorDimSetting> monitorSettings,
        string existingTargetDeviceNames)
    {
        HashSet<string> selectedDeviceNames = RuleTargetParser.ParseTargetDeviceNames(existingTargetDeviceNames);
        ObservableCollection<DisplayTargetOption> displayTargets = [];
        foreach (MonitorDimSetting monitorSetting in monitorSettings)
        {
            displayTargets.Add(new DisplayTargetOption(
                monitorSetting.DeviceName,
                monitorSetting.DisplayName,
                selectedDeviceNames.Contains(monitorSetting.DeviceName)));
        }

        return displayTargets;
    }

    private void UpdateRuleTargetDeviceNames()
    {
        List<string> selectedDeviceNames = DisplayTargets
            .Where(displayTarget => displayTarget.IsSelected)
            .Select(displayTarget => displayTarget.DeviceName)
            .ToList();
        _ruleDraft.TargetDeviceName = string.Join(";", selectedDeviceNames);
    }
}

public sealed class DisplayTargetOption : INotifyPropertyChanged
{
    private bool _isSelected;

    public DisplayTargetOption(string deviceName, string displayName, bool isSelected)
    {
        DeviceName = deviceName;
        DisplayName = displayName;
        _isSelected = isSelected;
    }

    public string DeviceName { get; }

    public string DisplayName { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
            {
                return;
            }

            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
