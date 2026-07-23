using NativeScreenDimmer_WinUI3.Models;

namespace NativeScreenDimmer_WinUI3.Services;

internal static class BlueLightService
{
    public static (string ColorHex, int MinimumDimPercent) ApplyMode(string baseColorHex, BlueLightMode blueLightMode)
    {
        System.Drawing.Color baseColor = ParseColor(baseColorHex);
        return blueLightMode switch
        {
            BlueLightMode.Gentle => (ToHex(Blend(baseColor, System.Drawing.Color.FromArgb(255, 180, 120), 0.28)), 10),
            BlueLightMode.Warm => (ToHex(Blend(baseColor, System.Drawing.Color.FromArgb(255, 150, 70), 0.48)), 16),
            BlueLightMode.Amber => (ToHex(Blend(baseColor, System.Drawing.Color.FromArgb(255, 120, 20), 0.62)), 22),
            BlueLightMode.Red => (ToHex(Blend(baseColor, System.Drawing.Color.FromArgb(255, 60, 0), 0.74)), 28),
            _ => (baseColorHex, 0)
        };
    }

    private static System.Drawing.Color Blend(System.Drawing.Color firstColor, System.Drawing.Color secondColor, double ratio)
    {
        double clampedRatio = Math.Clamp(ratio, 0, 1);
        double inverseRatio = 1 - clampedRatio;

        return System.Drawing.Color.FromArgb(
            (byte)Math.Clamp((firstColor.R * inverseRatio) + (secondColor.R * clampedRatio), byte.MinValue, byte.MaxValue),
            (byte)Math.Clamp((firstColor.G * inverseRatio) + (secondColor.G * clampedRatio), byte.MinValue, byte.MaxValue),
            (byte)Math.Clamp((firstColor.B * inverseRatio) + (secondColor.B * clampedRatio), byte.MinValue, byte.MaxValue));
    }

    private static System.Drawing.Color ParseColor(string colorHex)
    {
        if (string.IsNullOrWhiteSpace(colorHex))
        {
            return System.Drawing.Color.Black;
        }

        string candidate = colorHex.Trim();
        if (candidate.StartsWith('#') && candidate.Length == 7)
        {
            bool isValidHex = candidate[1..].All(character => Uri.IsHexDigit(character));
            if (isValidHex)
            {
                return System.Drawing.ColorTranslator.FromHtml(candidate);
            }
        }

        System.Drawing.Color namedColor = System.Drawing.Color.FromName(candidate);
        return namedColor.IsKnownColor ? namedColor : System.Drawing.Color.Black;
    }

    private static string ToHex(System.Drawing.Color color)
    {
        return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
    }
}
