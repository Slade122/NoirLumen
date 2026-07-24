using NativeScreenDimmer_WinUI3.Models;

namespace NativeScreenDimmer_WinUI3.Services;

public sealed class RuleEngine
{
    public bool IsRuleConditionActive(AutoRule rule, DateTime now, IReadOnlySet<string> runningProcessNames)
    {
        if (!rule.IsEnabled)
        {
            return false;
        }

        return IsRuleActive(rule, now, runningProcessNames);
    }

    public List<MonitorDimSetting> BuildEffectiveSettings(
        IReadOnlyList<MonitorDimSetting> baseSettings,
        IReadOnlyList<AutoRule> rules,
        DateTime now,
        IReadOnlySet<string> runningProcessNames)
    {
        using IDisposable operation = AppLogger.BeginOperation(nameof(BuildEffectiveSettings));
        List<MonitorDimSetting> effectiveSettings = baseSettings.Select(setting => setting.Clone()).ToList();
        if (rules.Count == 0)
        {
            AppLogger.LogInfo("Rule engine skipped: no rules configured.");
            return effectiveSettings;
        }

        List<AutoRule> activeRules = rules
            .Where(rule => rule.IsEnabled && IsRuleActive(rule, now, runningProcessNames))
            .OrderByDescending(rule => rule.Priority)
            .ToList();

        AppLogger.LogInfo($"Rule engine activeRules={activeRules.Count} totalRules={rules.Count}.");

        Dictionary<string, int> targetPriorityByDevice = new(StringComparer.OrdinalIgnoreCase);
        int skippedTargetAssignments = 0;
        foreach (AutoRule activeRule in activeRules)
        {
            IEnumerable<MonitorDimSetting> targetSettings = GetTargets(effectiveSettings, activeRule);
            foreach (MonitorDimSetting target in targetSettings)
            {
                if (targetPriorityByDevice.TryGetValue(target.DeviceName, out int winningPriority)
                    && winningPriority >= activeRule.Priority)
                {
                    skippedTargetAssignments++;
                    continue;
                }

                targetPriorityByDevice[target.DeviceName] = activeRule.Priority;
                target.IsEnabled = true;
                target.DimPercent = Math.Clamp(activeRule.DimPercent, 0, 95);
                target.ColorHex = string.IsNullOrWhiteSpace(activeRule.ColorHex)
                    ? target.ColorHex
                    : activeRule.ColorHex.Trim();
            }
        }

        if (skippedTargetAssignments > 0)
        {
            AppLogger.LogInfo($"Rule engine conflicts resolved skippedTargets={skippedTargetAssignments} winningTargets={targetPriorityByDevice.Count}.");
        }

        return effectiveSettings;
    }

    private static IEnumerable<MonitorDimSetting> GetTargets(
        IReadOnlyList<MonitorDimSetting> settings,
        AutoRule rule)
    {
        if (rule.TargetScope == RuleTargetScope.AllScreens)
        {
            return settings;
        }

        HashSet<string> targetDeviceNames = RuleTargetParser.ParseTargetDeviceNames(rule.TargetDeviceName);
        if (targetDeviceNames.Count == 0)
        {
            return [];
        }

        return settings.Where(setting => targetDeviceNames.Contains(setting.DeviceName));
    }

    private static bool IsRuleActive(AutoRule rule, DateTime now, IReadOnlySet<string> runningProcessNames)
    {
        return rule.Type switch
        {
            RuleType.TimeRange => IsTimeRangeActive(rule, now),
            RuleType.ProcessRunning => IsProcessRuleActive(rule, runningProcessNames),
            _ => false
        };
    }

    private static bool IsTimeRangeActive(AutoRule rule, DateTime now)
    {
        if (!TimeOnly.TryParse(rule.StartTime, out TimeOnly startTime))
        {
            return false;
        }

        if (!TimeOnly.TryParse(rule.EndTime, out TimeOnly endTime))
        {
            return false;
        }

        TimeOnly currentTime = new(now.Hour, now.Minute);
        if (startTime == endTime)
        {
            return true;
        }

        if (startTime < endTime)
        {
            return currentTime >= startTime && currentTime <= endTime;
        }

        return currentTime >= startTime || currentTime <= endTime;
    }

    private static bool IsProcessRuleActive(AutoRule rule, IReadOnlySet<string> runningProcessNames)
    {
        if (string.IsNullOrWhiteSpace(rule.ProcessName))
        {
            return false;
        }

        string normalized = Path.GetFileNameWithoutExtension(rule.ProcessName.Trim());
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        return runningProcessNames.Contains(normalized);
    }
}
