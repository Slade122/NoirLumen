using System.Diagnostics;
using NativeScreenDimmer_WinUI3.Models;
using NativeScreenDimmer_WinUI3.Services;

namespace NativeScreenDimmer.WinUI3.Tests;

public sealed class RuleEngineTests
{
    private readonly RuleEngine _ruleEngine = new();

    [Fact]
    public void HigherPriorityRuleWinsForTheSameDisplay()
    {
        List<MonitorDimSetting> settings =
        [
            new() { DeviceName = @"\\.\DISPLAY1", DisplayName = "One", DimPercent = 10, ColorHex = "#111111" }
        ];

        List<AutoRule> rules =
        [
            new()
            {
                Name = "Low",
                Type = RuleType.TimeRange,
                StartTime = "00:00",
                EndTime = "23:59",
                TargetScope = RuleTargetScope.AllScreens,
                DimPercent = 20,
                ColorHex = "#222222",
                Priority = 10
            },
            new()
            {
                Name = "High",
                Type = RuleType.TimeRange,
                StartTime = "00:00",
                EndTime = "23:59",
                TargetScope = RuleTargetScope.AllScreens,
                DimPercent = 80,
                ColorHex = "#AA0000",
                Priority = 100
            }
        ];

        List<MonitorDimSetting> result = _ruleEngine.BuildEffectiveSettings(settings, rules, DateTime.Now, new HashSet<string>());

        Assert.Equal(80, result[0].DimPercent);
        Assert.Equal("#AA0000", result[0].ColorHex);
    }

    [Fact]
    public void SpecificScreenTargetsMultipleScreens()
    {
        List<MonitorDimSetting> settings =
        [
            new() { DeviceName = @"\\.\DISPLAY1", DisplayName = "One" },
            new() { DeviceName = @"\\.\DISPLAY2", DisplayName = "Two" },
            new() { DeviceName = @"\\.\DISPLAY3", DisplayName = "Three" }
        ];

        AutoRule rule = new()
        {
            Name = "Two screens",
            Type = RuleType.TimeRange,
            StartTime = "00:00",
            EndTime = "23:59",
            TargetScope = RuleTargetScope.SpecificScreen,
            TargetDeviceName = @"1;3",
            DimPercent = 55,
            Priority = 50
        };

        List<MonitorDimSetting> result = _ruleEngine.BuildEffectiveSettings(settings, [rule], DateTime.Now, new HashSet<string>());

        Assert.Equal(55, result[0].DimPercent);
        Assert.Equal(0, result[1].DimPercent);
        Assert.Equal(55, result[2].DimPercent);
    }

    [Fact]
    public void PerformanceSmokeTestStaysFast()
    {
        List<MonitorDimSetting> settings =
        [
            new() { DeviceName = @"\\.\DISPLAY1", DisplayName = "One" },
            new() { DeviceName = @"\\.\DISPLAY2", DisplayName = "Two" },
            new() { DeviceName = @"\\.\DISPLAY3", DisplayName = "Three" }
        ];

        List<AutoRule> rules =
        [
            new()
            {
                Name = "Night",
                Type = RuleType.TimeRange,
                StartTime = "00:00",
                EndTime = "23:59",
                TargetScope = RuleTargetScope.AllScreens,
                DimPercent = 55,
                Priority = 100
            }
        ];

        Stopwatch stopwatch = Stopwatch.StartNew();
        for (int index = 0; index < 2_500; index++)
        {
            _ = _ruleEngine.BuildEffectiveSettings(settings, rules, DateTime.Now, new HashSet<string>());
        }

        stopwatch.Stop();

        Assert.True(stopwatch.ElapsedMilliseconds < 2500, $"BuildEffectiveSettings took {stopwatch.ElapsedMilliseconds}ms.");
    }
}
