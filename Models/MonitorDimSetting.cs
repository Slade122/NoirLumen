using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace NativeScreenDimmer_WinUI3.Models;

public sealed class MonitorDimSetting : INotifyPropertyChanged
{
    private bool _isEnabled;
    private int _dimPercent;
    private string _colorHex = "#000000";
    private BlueLightMode _blueLightMode;
    private bool _sunMimicEnabled = true;

    public string DeviceName { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public bool IsEnabled
    {
        get => _isEnabled;
        set => SetField(ref _isEnabled, value);
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

    public BlueLightMode BlueLightMode
    {
        get => _blueLightMode;
        set => SetField(ref _blueLightMode, value);
    }

    public bool SunMimicEnabled
    {
        get => _sunMimicEnabled;
        set => SetField(ref _sunMimicEnabled, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public MonitorDimSetting Clone()
    {
        return new MonitorDimSetting
        {
            DeviceName = DeviceName,
            DisplayName = DisplayName,
            IsEnabled = IsEnabled,
            DimPercent = DimPercent,
            ColorHex = ColorHex,
            BlueLightMode = BlueLightMode,
            SunMimicEnabled = SunMimicEnabled
        };
    }

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

