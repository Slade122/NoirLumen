namespace NativeScreenDimmer_WinUI3.Services;

internal sealed class SunCycleService
{
    private const double ZenithDegrees = 90.83333333333333;
    private DateTime _lastStatusLogUtc = DateTime.MinValue;
    private int _lastLoggedDimPercent = -1;

    public int CalculateSunMimicDimPercent(DateTime now, double latitude, double longitude, int maximumDimPercent)
    {
        if (maximumDimPercent <= 0)
        {
            AppLogger.LogWarning("Sun mimic max dim percent was <= 0.");
            return 0;
        }

        if (!TryGetSunTimes(now.Date, latitude, longitude, TimeZoneInfo.Local, out DateTime sunriseLocal, out DateTime sunsetLocal))
        {
            AppLogger.LogWarning($"Sun mimic sun-time calculation failed for lat={latitude:F4}, lon={longitude:F4}.");
            return 0;
        }

        DateTime solarNoon = sunriseLocal.AddTicks((sunsetLocal - sunriseLocal).Ticks / 2);
        DateTime sunset = sunsetLocal;
        DateTime fullNightStart = sunsetLocal.AddMinutes(90);
        DateTime fullNightEnd = sunriseLocal.AddDays(1).AddMinutes(-90);
        DateTime sunriseNext = sunriseLocal.AddDays(1);
        DateTime dawnEnd = sunriseNext.AddMinutes(90);
        DateTime effectiveNow = now < sunriseLocal ? now.AddDays(1) : now;

        double nightFactor;
        if (effectiveNow >= fullNightStart && effectiveNow <= fullNightEnd)
        {
            nightFactor = 1;
        }
        else if (effectiveNow >= sunset && effectiveNow < fullNightStart)
        {
            double progressToNight = (effectiveNow - sunset).TotalMinutes / 90.0;
            nightFactor = 0.60 + (Math.Clamp(progressToNight, 0, 1) * 0.40);
        }
        else if (effectiveNow >= solarNoon && effectiveNow < sunset)
        {
            double sunsetApproachProgress = (effectiveNow - solarNoon).TotalMinutes / (sunset - solarNoon).TotalMinutes;
            nightFactor = Math.Clamp(sunsetApproachProgress, 0, 1) * 0.60;
        }
        else if (effectiveNow > fullNightEnd && effectiveNow <= sunriseNext)
        {
            double dawnProgress = (sunriseNext - effectiveNow).TotalMinutes / 90.0;
            nightFactor = 0.25 + (Math.Clamp(dawnProgress, 0, 1) * 0.75);
        }
        else if (effectiveNow > sunriseNext && effectiveNow <= dawnEnd)
        {
            nightFactor = (dawnEnd - effectiveNow).TotalMinutes / 180.0;
        }
        else
        {
            nightFactor = 0;
        }

        int calculatedDimPercent = Math.Clamp((int)Math.Round(Math.Clamp(nightFactor, 0, 1) * maximumDimPercent), 0, 95);
        if (_lastLoggedDimPercent != calculatedDimPercent || (DateTime.UtcNow - _lastStatusLogUtc).TotalSeconds >= 60)
        {
            _lastLoggedDimPercent = calculatedDimPercent;
            _lastStatusLogUtc = DateTime.UtcNow;
            AppLogger.LogInfo($"Sun mimic calculated dim={calculatedDimPercent}% at localTime={now:O}.");
        }

        return calculatedDimPercent;
    }

    private static bool TryGetSunTimes(
        DateTime date,
        double latitude,
        double longitude,
        TimeZoneInfo timeZoneInfo,
        out DateTime sunriseLocal,
        out DateTime sunsetLocal)
    {
        sunriseLocal = date;
        sunsetLocal = date;

        if (latitude is < -89.9 or > 89.9 || longitude is < -180 or > 180)
        {
            return false;
        }

        int dayOfYear = date.DayOfYear;
        double? sunriseHour = CalculateLocalSolarHour(dayOfYear, latitude, longitude, true, timeZoneInfo, date);
        double? sunsetHour = CalculateLocalSolarHour(dayOfYear, latitude, longitude, false, timeZoneInfo, date);
        if (sunriseHour is null || sunsetHour is null)
        {
            return false;
        }

        sunriseLocal = date.Date.AddHours(sunriseHour.Value);
        sunsetLocal = date.Date.AddHours(sunsetHour.Value);
        return true;
    }

    private static double? CalculateLocalSolarHour(
        int dayOfYear,
        double latitude,
        double longitude,
        bool isSunrise,
        TimeZoneInfo timeZoneInfo,
        DateTime date)
    {
        double longitudeHour = longitude / 15.0;
        double approximateTime = dayOfYear + ((isSunrise ? 6.0 : 18.0) - longitudeHour) / 24.0;
        double sunMeanAnomaly = (0.9856 * approximateTime) - 3.289;
        double sunTrueLongitude = sunMeanAnomaly + (1.916 * SinDeg(sunMeanAnomaly)) + (0.020 * SinDeg(2 * sunMeanAnomaly)) + 282.634;
        sunTrueLongitude = NormalizeDegrees(sunTrueLongitude);

        double rightAscension = RadToDeg(Math.Atan(0.91764 * TanDeg(sunTrueLongitude)));
        rightAscension = NormalizeDegrees(rightAscension);

        double rightAscensionQuadrant = Math.Floor(rightAscension / 90) * 90;
        double longitudeQuadrant = Math.Floor(sunTrueLongitude / 90) * 90;
        rightAscension += longitudeQuadrant - rightAscensionQuadrant;
        rightAscension /= 15;

        double sinDeclination = 0.39782 * SinDeg(sunTrueLongitude);
        double cosDeclination = Math.Cos(Math.Asin(sinDeclination));
        double cosHourAngle = (CosDeg(ZenithDegrees) - (sinDeclination * SinDeg(latitude))) / (cosDeclination * CosDeg(latitude));

        if (cosHourAngle is > 1 or < -1)
        {
            return null;
        }

        double hourAngle = isSunrise ? 360 - RadToDeg(Math.Acos(cosHourAngle)) : RadToDeg(Math.Acos(cosHourAngle));
        hourAngle /= 15;

        double localMeanTime = hourAngle + rightAscension - (0.06571 * approximateTime) - 6.622;
        double universalTime = NormalizeHours(localMeanTime - longitudeHour);
        double offsetHours = timeZoneInfo.GetUtcOffset(date).TotalHours;
        return NormalizeHours(universalTime + offsetHours);
    }

    private static double SinDeg(double degrees) => Math.Sin(DegToRad(degrees));

    private static double CosDeg(double degrees) => Math.Cos(DegToRad(degrees));

    private static double TanDeg(double degrees) => Math.Tan(DegToRad(degrees));

    private static double DegToRad(double degrees) => degrees * Math.PI / 180.0;

    private static double RadToDeg(double radians) => radians * 180.0 / Math.PI;

    private static double NormalizeDegrees(double degrees)
    {
        double normalized = degrees % 360.0;
        return normalized < 0 ? normalized + 360 : normalized;
    }

    private static double NormalizeHours(double hours)
    {
        double normalized = hours % 24.0;
        return normalized < 0 ? normalized + 24 : normalized;
    }
}

