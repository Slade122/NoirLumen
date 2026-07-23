using System.Text.Json;
using System.Text.Json.Serialization;
using System.Net.Http;
using System.Globalization;

namespace NativeScreenDimmer_WinUI3.Services;

internal sealed class LocationService
{
    private static readonly Uri PrimaryEndpointUri = new("https://ipwho.is/");
    private static readonly Uri SecondaryEndpointUri = new("https://ipapi.co/json/");

    public async Task<(double Latitude, double Longitude)?> TryGetCurrentCoordinatesAsync(TimeSpan timeout)
    {
        AppLogger.LogInfo($"Starting location auto-detect with timeout={timeout.TotalSeconds:F1}s.");

        using CancellationTokenSource timeoutSource = new(timeout);
        using HttpClient client = new()
        {
            Timeout = timeout
        };

        client.DefaultRequestHeaders.UserAgent.ParseAdd("NativeScreenDimmer/1.0");

        (double Latitude, double Longitude)? primaryResult = await TryGetIpWhoCoordinatesAsync(client, timeoutSource.Token);
        if (primaryResult is not null)
        {
            AppLogger.LogInfo($"Location provider ipwho.is succeeded lat={primaryResult.Value.Latitude:F4}, lon={primaryResult.Value.Longitude:F4}.");
            return primaryResult.Value;
        }

        (double Latitude, double Longitude)? secondaryResult = await TryGetIpApiCoordinatesAsync(client, timeoutSource.Token);
        if (secondaryResult is not null)
        {
            AppLogger.LogInfo($"Location provider ipapi.co succeeded lat={secondaryResult.Value.Latitude:F4}, lon={secondaryResult.Value.Longitude:F4}.");
            return secondaryResult.Value;
        }

        (double Latitude, double Longitude)? tertiaryResult = await TryGetIpInfoCoordinatesAsync(client, timeoutSource.Token);
        if (tertiaryResult is not null)
        {
            AppLogger.LogInfo($"Location provider ipinfo.io succeeded lat={tertiaryResult.Value.Latitude:F4}, lon={tertiaryResult.Value.Longitude:F4}.");
            return tertiaryResult.Value;
        }

        AppLogger.LogWarning("Location auto-detect failed across all providers.");
        return null;
    }

    private static async Task<(double Latitude, double Longitude)?> TryGetIpWhoCoordinatesAsync(HttpClient client, CancellationToken cancellationToken)
    {
        try
        {
            using HttpResponseMessage response = await client.GetAsync(PrimaryEndpointUri, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                AppLogger.LogWarning($"ipwho.is returned HTTP {(int)response.StatusCode}.");
                return null;
            }

            string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            IpWhoResponse? locationResponse = JsonSerializer.Deserialize<IpWhoResponse>(responseBody);
            if (locationResponse is null || locationResponse.Success != true)
            {
                AppLogger.LogWarning("ipwho.is returned an unsuccessful payload.");
                return null;
            }

            if (locationResponse.Latitude is null || locationResponse.Longitude is null)
            {
                AppLogger.LogWarning("ipwho.is payload had missing coordinates.");
                return null;
            }

            return (locationResponse.Latitude.Value, locationResponse.Longitude.Value);
        }
        catch (TaskCanceledException exception)
        {
            AppLogger.LogException("ipwho.is timeout/cancelled", exception);
            return null;
        }
        catch (HttpRequestException exception)
        {
            AppLogger.LogException("ipwho.is request failed", exception);
            return null;
        }
        catch (JsonException exception)
        {
            AppLogger.LogException("ipwho.is JSON parsing failed", exception);
            return null;
        }
    }

    private static async Task<(double Latitude, double Longitude)?> TryGetIpApiCoordinatesAsync(HttpClient client, CancellationToken cancellationToken)
    {
        try
        {
            using HttpResponseMessage response = await client.GetAsync(SecondaryEndpointUri, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                AppLogger.LogWarning($"ipapi.co returned HTTP {(int)response.StatusCode}.");
                return null;
            }

            string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            IpApiResponse? locationResponse = JsonSerializer.Deserialize<IpApiResponse>(responseBody);
            if (locationResponse?.Latitude is null || locationResponse.Longitude is null)
            {
                AppLogger.LogWarning("ipapi.co payload had missing coordinates.");
                return null;
            }

            return (locationResponse.Latitude.Value, locationResponse.Longitude.Value);
        }
        catch (TaskCanceledException exception)
        {
            AppLogger.LogException("ipapi.co timeout/cancelled", exception);
            return null;
        }
        catch (HttpRequestException exception)
        {
            AppLogger.LogException("ipapi.co request failed", exception);
            return null;
        }
        catch (JsonException exception)
        {
            AppLogger.LogException("ipapi.co JSON parsing failed", exception);
            return null;
        }
    }

    private static async Task<(double Latitude, double Longitude)?> TryGetIpInfoCoordinatesAsync(HttpClient client, CancellationToken cancellationToken)
    {
        try
        {
            using HttpResponseMessage response = await client.GetAsync("https://ipinfo.io/json", cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                AppLogger.LogWarning($"ipinfo.io returned HTTP {(int)response.StatusCode}.");
                return null;
            }

            string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            IpInfoResponse? locationResponse = JsonSerializer.Deserialize<IpInfoResponse>(responseBody);
            if (string.IsNullOrWhiteSpace(locationResponse?.Coordinates))
            {
                AppLogger.LogWarning("ipinfo.io payload had missing coordinates.");
                return null;
            }

            string[] parts = locationResponse.Coordinates.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2)
            {
                AppLogger.LogWarning("ipinfo.io coordinates had an invalid format.");
                return null;
            }

            if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double latitude))
            {
                AppLogger.LogWarning("ipinfo.io latitude parse failed.");
                return null;
            }

            if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double longitude))
            {
                AppLogger.LogWarning("ipinfo.io longitude parse failed.");
                return null;
            }

            return (latitude, longitude);
        }
        catch (TaskCanceledException exception)
        {
            AppLogger.LogException("ipinfo.io timeout/cancelled", exception);
            return null;
        }
        catch (HttpRequestException exception)
        {
            AppLogger.LogException("ipinfo.io request failed", exception);
            return null;
        }
        catch (JsonException exception)
        {
            AppLogger.LogException("ipinfo.io JSON parsing failed", exception);
            return null;
        }
    }

    private sealed class IpWhoResponse
    {
        public bool? Success { get; set; }

        [JsonPropertyName("lat")]
        public double? Latitude { get; set; }

        [JsonPropertyName("lon")]
        public double? Longitude { get; set; }
    }

    private sealed class IpApiResponse
    {
        [JsonPropertyName("latitude")]
        public double? Latitude { get; set; }

        [JsonPropertyName("longitude")]
        public double? Longitude { get; set; }
    }

    private sealed class IpInfoResponse
    {
        [JsonPropertyName("loc")]
        public string? Coordinates { get; set; }
    }
}

