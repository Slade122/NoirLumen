using NativeScreenDimmer_WinUI3.Models;

namespace NativeScreenDimmer_WinUI3.Services;

internal sealed class RuleValidationService
{
    public RuleValidationResult Validate(AutoRule rule, IReadOnlyList<MonitorDimSetting> monitorSettings)
    {
        List<string> issues = [];

        if (string.IsNullOrWhiteSpace(rule.Name))
        {
            issues.Add("Name is required.");
        }

        if (rule.DimPercent is < 0 or > 95)
        {
            issues.Add("Dim percent must be between 0 and 95.");
        }

        if (rule.Priority < int.MinValue / 2)
        {
            issues.Add("Priority is too low.");
        }

        switch (rule.Type)
        {
            case RuleType.TimeRange:
                if (!TimeOnly.TryParse(rule.StartTime, out _))
                {
                    issues.Add("Start time must use HH:mm.");
                }

                if (!TimeOnly.TryParse(rule.EndTime, out _))
                {
                    issues.Add("End time must use HH:mm.");
                }
                break;
            case RuleType.ProcessRunning:
                if (string.IsNullOrWhiteSpace(rule.ProcessName))
                {
                    issues.Add("Process name is required for app rules.");
                }
                break;
        }

        if (rule.TargetScope == RuleTargetScope.SpecificScreen)
        {
            HashSet<string> selectedDeviceNames = RuleTargetParser.ParseTargetDeviceNames(rule.TargetDeviceName);
            if (selectedDeviceNames.Count == 0)
            {
                issues.Add("Pick at least one screen.");
            }
            else if (monitorSettings.Count > 0)
            {
                HashSet<string> availableDeviceNames = new(monitorSettings.Select(setting => setting.DeviceName), StringComparer.OrdinalIgnoreCase);
                if (selectedDeviceNames.Any(deviceName => !availableDeviceNames.Contains(deviceName)))
                {
                    issues.Add("One or more selected screens are not available.");
                }
            }
        }

        return new RuleValidationResult(issues.Count == 0, issues, BuildPreviewText(rule, monitorSettings));
    }

    public string BuildPreviewText(AutoRule rule, IReadOnlyList<MonitorDimSetting> monitorSettings)
    {
        string targetText = rule.TargetScope == RuleTargetScope.AllScreens
            ? "all displays"
            : DescribeSpecificTargets(rule.TargetDeviceName, monitorSettings);

        string conditionText = rule.Type switch
        {
            RuleType.TimeRange => $"when local time is between {rule.StartTime} and {rule.EndTime}",
            RuleType.ProcessRunning => string.IsNullOrWhiteSpace(rule.ProcessName)
                ? "when the process is running"
                : $"when {System.IO.Path.GetFileNameWithoutExtension(rule.ProcessName.Trim())} is running",
            _ => "when it matches"
        };

        return $"Applies to {targetText} {conditionText}.";
    }

    private static string DescribeSpecificTargets(string targetDeviceNames, IReadOnlyList<MonitorDimSetting> monitorSettings)
    {
        HashSet<string> selectedDeviceNames = RuleTargetParser.ParseTargetDeviceNames(targetDeviceNames);
        if (selectedDeviceNames.Count == 0 || monitorSettings.Count == 0)
        {
            return "selected screens";
        }

        List<string> selectedDisplayNames = monitorSettings
            .Where(setting => selectedDeviceNames.Contains(setting.DeviceName))
            .Select(setting => string.IsNullOrWhiteSpace(setting.DisplayName) ? setting.DeviceName : setting.DisplayName)
            .ToList();

        return selectedDisplayNames.Count == 0
            ? "selected screens"
            : string.Join(", ", selectedDisplayNames);
    }
}

internal sealed record RuleValidationResult(bool IsValid, IReadOnlyList<string> Issues, string PreviewText);
