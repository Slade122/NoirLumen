namespace NativeScreenDimmer_WinUI3.Services;

public static class RuleTargetParser
{
    public static HashSet<string> ParseTargetDeviceNames(string targetDeviceNames)
    {
        HashSet<string> normalizedDeviceNames = new(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(targetDeviceNames))
        {
            return normalizedDeviceNames;
        }

        string[] tokens = targetDeviceNames
            .Split([',', ';', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (string token in tokens)
        {
            string normalizedToken = NormalizeTargetDeviceNameToken(token);
            if (!string.IsNullOrWhiteSpace(normalizedToken))
            {
                normalizedDeviceNames.Add(normalizedToken);
            }
        }

        return normalizedDeviceNames;
    }

    public static string NormalizeTargetDeviceNameToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return string.Empty;
        }

        string trimmedToken = token.Trim();
        if (trimmedToken.StartsWith(@"\\.\DISPLAY", StringComparison.OrdinalIgnoreCase))
        {
            return @"\\.\DISPLAY" + trimmedToken[11..];
        }

        if (int.TryParse(trimmedToken, out int displayIndex) && displayIndex > 0)
        {
            return $@"\\.\DISPLAY{displayIndex}";
        }

        return trimmedToken;
    }
}
