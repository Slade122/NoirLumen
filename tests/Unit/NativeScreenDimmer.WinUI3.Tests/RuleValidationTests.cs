using NativeScreenDimmer_WinUI3.Models;
using NativeScreenDimmer_WinUI3.Services;

namespace NativeScreenDimmer.WinUI3.Tests;

public sealed class RuleValidationTests
{
    private readonly RuleValidationService _validationService = new();

    [Fact]
    public void RequiresAtLeastOneSelectedScreenForSpecificScope()
    {
        AutoRule rule = new()
        {
            Name = "Test",
            Type = RuleType.TimeRange,
            StartTime = "21:00",
            EndTime = "06:00",
            TargetScope = RuleTargetScope.SpecificScreen
        };

        RuleValidationResult result = _validationService.Validate(rule, []);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, issue => issue.Contains("Pick at least one screen", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BuildsHumanReadablePreviewText()
    {
        AutoRule rule = new()
        {
            Name = "Night",
            Type = RuleType.ProcessRunning,
            ProcessName = "game.exe",
            TargetScope = RuleTargetScope.AllScreens
        };

        RuleValidationResult result = _validationService.Validate(rule, []);

        Assert.Contains("all displays", result.PreviewText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("game", result.PreviewText, StringComparison.OrdinalIgnoreCase);
        Assert.True(result.IsValid);
    }
}
