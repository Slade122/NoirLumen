using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace NativeScreenDimmer_WinUI3.Models;

public enum RuleType
{
    TimeRange = 0,
    ProcessRunning = 1
}

public enum RuleTargetScope
{
    AllScreens = 0,
    SpecificScreen = 1
}

public sealed class AutoRule : INotifyPropertyChanged
{
    private string _name = "Rule";
    private bool _isEnabled = true;
    private RuleType _type = RuleType.TimeRange;
    private RuleTargetScope _targetScope;
    private string _targetDeviceName = string.Empty;
    private string _startTime = "21:00";
    private string _endTime = "06:00";
    private string _processName = string.Empty;
    private int _dimPercent = 55;
    private string _colorHex = "#000000";
    private int _priority = 100;
    private bool _isRuntimeActive;

    public string Name
    {
        get => _name;
        set => SetField(ref _name, value);
    }

    public bool IsEnabled
    {
        get => _isEnabled;
        set => SetField(ref _isEnabled, value);
    }

    public RuleType Type
    {
        get => _type;
        set => SetField(ref _type, value);
    }

    public RuleTargetScope TargetScope
    {
        get => _targetScope;
        set => SetField(ref _targetScope, value);
    }

    public string TargetDeviceName
    {
        get => _targetDeviceName;
        set => SetField(ref _targetDeviceName, value);
    }

    public string StartTime
    {
        get => _startTime;
        set => SetField(ref _startTime, value);
    }

    public string EndTime
    {
        get => _endTime;
        set => SetField(ref _endTime, value);
    }

    public string ProcessName
    {
        get => _processName;
        set => SetField(ref _processName, value);
    }

    public int DimPercent
    {
        get => _dimPercent;
        set => SetField(ref _dimPercent, Math.Clamp(value, 0, 95));
    }

    public string ColorHex
    {
        get => _colorHex;
        set => SetField(ref _colorHex, string.IsNullOrWhiteSpace(value) ? "#000000" : value.Trim());
    }

    public int Priority
    {
        get => _priority;
        set => SetField(ref _priority, value);
    }

    [JsonIgnore]
    public bool IsRuntimeActive
    {
        get => _isRuntimeActive;
        set
        {
            if (_isRuntimeActive == value)
            {
                return;
            }

            _isRuntimeActive = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsRuntimeActive)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RuntimeStateLabel)));
        }
    }

    [JsonIgnore]
    public string RuntimeStateLabel => IsRuntimeActive ? "Active now" : "Waiting";

    public AutoRule Clone()
    {
        return new AutoRule
        {
            Name = Name,
            IsEnabled = IsEnabled,
            Type = Type,
            TargetScope = TargetScope,
            TargetDeviceName = TargetDeviceName,
            StartTime = StartTime,
            EndTime = EndTime,
            ProcessName = ProcessName,
            DimPercent = DimPercent,
            ColorHex = ColorHex,
            Priority = Priority
        };
    }

    public void CopyFrom(AutoRule other)
    {
        Name = other.Name;
        IsEnabled = other.IsEnabled;
        Type = other.Type;
        TargetScope = other.TargetScope;
        TargetDeviceName = other.TargetDeviceName;
        StartTime = other.StartTime;
        EndTime = other.EndTime;
        ProcessName = other.ProcessName;
        DimPercent = other.DimPercent;
        ColorHex = other.ColorHex;
        Priority = other.Priority;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
