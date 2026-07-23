using NativeScreenDimmer_WinUI3.Models;

namespace NativeScreenDimmer_WinUI3.Services;

internal static class BlueLightService
{
    public static (string ColorHex, int MinimumDimPercent) ApplyMode(string baseColorHex, BlueLightMode blueLightMode)
    {
        System.Drawing.Color baseColor = ParseColor(baseColorHex);
        return blueLightMode switch
        {
            BlueLightMode.Gentle => ApplyTemperatureShift(baseColor, 4_600, 0.18, 6),
            BlueLightMode.Warm => ApplyTemperatureShift(baseColor, 3_800, 0.32, 10),
            BlueLightMode.Amber => ApplyTemperatureShift(baseColor, 3_000, 0.48, 16),
            BlueLightMode.Red => ApplyTemperatureShift(baseColor, 2_400, 0.64, 22),
            _ => (baseColorHex, 0)
        };
    }

    private static (string ColorHex, int MinimumDimPercent) ApplyTemperatureShift(
        System.Drawing.Color baseColor,
        int targetTemperatureKelvin,
        double blendRatio,
        int minimumDimPercent)
    {
        System.Drawing.Color targetColor = TemperatureToColor(targetTemperatureKelvin);
        System.Drawing.Color shiftedColor = Blend(baseColor, targetColor, blendRatio);
        return (ToHex(shiftedColor), minimumDimPercent);
    }

    private static System.Drawing.Color TemperatureToColor(int temperatureKelvin)
    {
        double clampedKelvin = Math.Clamp(temperatureKelvin, 1_000, 40_000) / 100.0;

        double red;
        double green;
        double blue;

        if (clampedKelvin <= 66)
        {
            red = 255;
            green = 99.4708025861 * Math.Log(clampedKelvin) - 161.1195681661;
            blue = clampedKelvin <= 19
                ? 0
                : 138.5177312231 * Math.Log(clampedKelvin - 10) - 305.0447927307;
        }
        else
        {
            red = 329.698727446 * Math.Pow(clampedKelvin - 60, -0.1332047592);
            green = 288.1221695283 * Math.Pow(clampedKelvin - 60, -0.0755148492);
            blue = 255;
        }

        return System.Drawing.Color.FromArgb(
            ClampChannel(red),
            ClampChannel(green),
            ClampChannel(blue));
    }

    private static byte ClampChannel(double value)
    {
        return (byte)Math.Clamp((int)Math.Round(value), byte.MinValue, byte.MaxValue);
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
