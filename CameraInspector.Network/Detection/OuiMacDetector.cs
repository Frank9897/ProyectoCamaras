using System.Collections.Concurrent;
using System.Net.Http;
using CameraInspector.Core.Interfaces;
using CameraInspector.Core.Models;

namespace CameraInspector.Network.Detection;

/// <summary>
/// Determina el fabricante a partir de la MAC/OUI. La MAC se obtiene localmente
/// mediante ARP y, cuando es válida para OUI, se consulta una base externa para
/// evitar depender de una tabla local incompleta.
/// Esta señal solo identifica fabricante: no confirma por sí sola que sea una cámara.
/// </summary>
public sealed class OuiMacDetector : IManufacturerDetector
{
    private const string ApiBaseUrl = "https://api.macvendors.com/";

    private static readonly Dictionary<string, string> LocalFallbackTable = new(StringComparer.OrdinalIgnoreCase)
    {
        ["4C:11:BF"] = "Hikvision",
        ["44:19:B6"] = "Hikvision",
        ["8C:E7:48"] = "Hikvision",
        ["BC:AD:28"] = "Dahua",
        ["3C:EF:8C"] = "Dahua",
        ["90:02:A9"] = "Dahua",
        ["00:40:8C"] = "Axis",
        ["AC:CC:8E"] = "Axis",
        ["EC:71:DB"] = "Uniview",
        ["24:0F:9D"] = "Uniview",
        ["EC:44:76"] = "Reolink",
    };

    private readonly HttpClient _httpClient;
    private readonly ConcurrentDictionary<string, Lazy<Task<string?>>> _vendorCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _apiGate = new(1, 1);
    private DateTimeOffset _lastApiRequestAt = DateTimeOffset.MinValue;

    public string Name => "OuiMac";

    public OuiMacDetector(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    public async Task<ManufacturerDetectionResult?> TryDetectAsync(
        DiscoveredDevice device, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!TryNormalizeMac(device.MacAddress, out var normalizedMac, out var oui))
        {
            if (IsLocallyAdministered(device.MacAddress))
            {
                device.AddEvidence(
                    Name,
                    0.05,
                    "MAC localmente administrada: el OUI no permite identificar de forma fiable el fabricante.",
                    false);
            }

            return null;
        }

        var manufacturer = await ResolveManufacturerAsync(normalizedMac, oui, cancellationToken);
        if (string.IsNullOrWhiteSpace(manufacturer))
            return null;

        return new ManufacturerDetectionResult
        {
            DetectorName = Name,
            Confidence = 0.50,
            EvidenceDetails = $"MAC {normalizedMac} · OUI {oui} · fabricante {manufacturer}",
            Manufacturer = manufacturer,
            CameraEvidence = false
        };
    }

    private async Task<string?> ResolveManufacturerAsync(
        string normalizedMac,
        string oui,
        CancellationToken cancellationToken)
    {
        // Una consulta por OUI. Todas las MAC del mismo fabricante reutilizan la respuesta.
        var lazy = _vendorCache.GetOrAdd(
            oui,
            _ => new Lazy<Task<string?>>(
                () => QueryApiAsync(normalizedMac, cancellationToken),
                LazyThreadSafetyMode.ExecutionAndPublication));

        return await lazy.Value;
    }

    private async Task<string?> QueryApiAsync(
        string normalizedMac,
        CancellationToken cancellationToken)
    {
        try
        {
            await _apiGate.WaitAsync(cancellationToken);
            try
            {
                var elapsed = DateTimeOffset.UtcNow - _lastApiRequestAt;
                if (elapsed < TimeSpan.FromSeconds(1))
                    await Task.Delay(TimeSpan.FromSeconds(1) - elapsed, cancellationToken);

                using var request = new HttpRequestMessage(
                    HttpMethod.Get,
                    ApiBaseUrl + Uri.EscapeDataString(normalizedMac));
                request.Headers.TryAddWithoutValidation("User-Agent", "CameraInspector/1.0");
                request.Headers.TryAddWithoutValidation("Accept", "text/plain");

                using var response = await _httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);

                _lastApiRequestAt = DateTimeOffset.UtcNow;

                if ((int)response.StatusCode == 404)
                    return null;

                if ((int)response.StatusCode == 429)
                    return LocalFallbackTable.TryGetValue(normalizedMac[..8], out var limitedFallback)
                        ? limitedFallback
                        : null;

                if (!response.IsSuccessStatusCode)
                    return LocalFallbackTable.TryGetValue(normalizedMac[..8], out var errorFallback)
                        ? errorFallback
                        : null;

                var vendor = (await response.Content.ReadAsStringAsync(cancellationToken)).Trim();
                return string.IsNullOrWhiteSpace(vendor)
                    ? LocalFallbackTable.GetValueOrDefault(normalizedMac[..8])
                    : vendor;
            }
            finally
            {
                _apiGate.Release();
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return LocalFallbackTable.GetValueOrDefault(normalizedMac[..8]);
        }
        catch (HttpRequestException)
        {
            return LocalFallbackTable.GetValueOrDefault(normalizedMac[..8]);
        }
        catch (TaskCanceledException)
        {
            return LocalFallbackTable.GetValueOrDefault(normalizedMac[..8]);
        }
    }

    private static bool TryNormalizeMac(
        string? value,
        out string normalizedMac,
        out string oui)
    {
        normalizedMac = string.Empty;
        oui = string.Empty;

        if (string.IsNullOrWhiteSpace(value))
            return false;

        var compact = value
            .Replace(":", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace(".", string.Empty, StringComparison.Ordinal)
            .Replace("/", string.Empty, StringComparison.Ordinal)
            .Trim();

        if (compact.Length != 12 || compact.Any(character => !Uri.IsHexDigit(character)))
            return false;

        // Bit I/G = 1 => multicast/group MAC, no OUI de fabricante unicast.
        var firstOctet = Convert.ToByte(compact[..2], 16);
        if ((firstOctet & 0x01) != 0)
            return false;

        // Bit U/L = 1 => MAC localmente administrada/virtual; no inferimos fabricante.
        if ((firstOctet & 0x02) != 0)
            return false;

        normalizedMac = string.Join(
            ":",
            Enumerable.Range(0, 6)
                .Select(index => compact.Substring(index * 2, 2).ToUpperInvariant()));
        oui = normalizedMac[..8];
        return true;
    }

    private static bool IsLocallyAdministered(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var compact = value.Replace(":", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace(".", string.Empty, StringComparison.Ordinal)
            .Trim();

        if (compact.Length < 2 || compact.Any(character => !Uri.IsHexDigit(character)))
            return false;

        var firstOctet = Convert.ToByte(compact[..2], 16);
        return (firstOctet & 0x02) != 0;
    }
}
