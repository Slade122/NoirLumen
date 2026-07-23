using System.Diagnostics;
using NativeScreenDimmer_WinUI3.Models;

namespace NativeScreenDimmer_WinUI3.Services;

internal sealed class RuleEngine
{
    public List<MonitorDimSetting> BuildEffectiveSettings(
        IReadOnlyList<MonitorDimSetting> baseSettings,
        IReadOnlyList<AutoRule> rules,
        DateTime now)
    {
        using IDisposable operation = AppLogger.BeginOperation(nameof(BuildEffectiveSettings));
        List<MonitorDimSetting> effectiveSettings = baseSettings.Select(setting => setting.Clone()).ToList();
        if (rules.Count == 0)
        {
            AppLogger.LogInfo("Rule engine skipped: no rules configured.");
            return effectiveSettings;
        }

        HashSet<string> runningProcessNames = GetRunningProcessNames(rules);
        List<AutoRule> activeRules = rules
            .Where(rule => rule.IsEnabled && IsRuleActive(rule, now, runningProcessNames))
            .OrderByDescending(rule => rule.Priority)
            .ToList();

        AppLogger.LogInfo($"Rule engine activeRules={activeRules.Count} totalRules={rules.Count}.");

        foreach (AutoRule activeRule in activeRules)
        {
            IEnumerable<MonitorDimSetting> targetSettings = GetTargets(effectiveSettings, activeRule);
            foreach (MonitorDimSetting target in targetSettings)
            {
                target.IsEnabled = true;
                target.DimPercent = Math.Max(target.DimPercent, Math.Clamp(activeRule.DimPercent, 0, 95));

                if (!string.IsNullOrWhiteSpace(activeRule.ColorHex))
                {
                    target.ColorHex = activeRule.ColorHex.Trim();
                }
            }
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

        return settings.Where(setting => string.Equals(
            setting.DeviceName,
            rule.TargetDeviceName,
            StringComparison.OrdinalIgnoreCase));
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

        TimeOnly currentTime = TimeOnly.FromDateTime(now);
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

        string normalized = System.IO.Path.GetFileNameWithoutExtension(rule.ProcessName.Trim());
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        return runningProcessNames.Contains(normalized);
    }

    private static HashSet<string> GetRunningProcessNames(IReadOnlyList<AutoRule> rules)
    {
        bool hasProcessRules = rules.Any(rule => rule.IsEnabled && rule.Type == RuleType.ProcessRunning);
        if (!hasProcessRules)
        {
            return [];
        }

        Process[] runningProcesses = Process.GetProcesses();
        try
        {
            HashSet<string> processNames = new(StringComparer.OrdinalIgnoreCase);
            foreach (Process process in runningProcesses)
            {
                string processName = process.ProcessName;
                if (!string.IsNullOrWhiteSpace(processName))
                {
                    processNames.Add(processName);
                }
            }

            return processNames;
        }
        finally
        {
            foreach (Process process in runningProcesses)
            {
                process.Dispose();
            }
        }
    }
}

