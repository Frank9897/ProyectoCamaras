using System.Net;
using System.Net.Http.Headers;
using System.Xml.Linq;
using System.Text.Json;
using CameraInspector.Core.Interfaces;
using CameraInspector.Core.Models;

namespace CameraInspector.Network.Detection;

/// <summary>
/// Detector HTTP orientado a cámaras antiguas o propietarias sin ONVIF.
/// Usa endpoints de lectura conocidos; una respuesta 401/403 de un endpoint específico
/// también constituye evidencia fuerte de que el servicio pertenece al fabricante.
/// </summary>
public sealed class LegacyCameraHttpDetector : IManufacturerDetector
{
    private static readonly string[] DefaultHttpPorts = { "80", "443", "8080", "8081", "8443" };
    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(1200);

    public string Name => "LegacyHttp";

    public async Task<ManufacturerDetectionResult?> TryDetectAsync(
        DiscoveredDevice device,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(device.IpAddress))
            return null;

        var ports = new List<int>();
        if (device.HttpPort is int configuredHttp)
            ports.Add(configuredHttp);
        ports.AddRange(DefaultHttpPorts.Select(int.Parse));
        ports = ports.Distinct().ToList();

        foreach (var port in ports)
        {
            cancellationToken.ThrowIfCancellationRequested();

            foreach (var scheme in port == 443 || port == 8443 ? new[] { "https" } : new[] { "http" })
            {
                var baseUri = $"{scheme}://{device.IpAddress}:{port}";

                var result = await ProbeHikvisionAsync(baseUri, cancellationToken);
                if (result is not null) return result;

                result = await ProbeDahuaAsync(baseUri, cancellationToken);
                if (result is not null) return result;

                result = await ProbeAxisAsync(baseUri, cancellationToken);
                if (result is not null) return result;

                result = await ProbeVivotekAsync(baseUri, cancellationToken);
                if (result is not null) return result;

                result = await ProbeReolinkAsync(baseUri, cancellationToken);
                if (result is not null) return result;
            }
        }

        return null;
    }

    private static async Task<ManufacturerDetectionResult?> ProbeHikvisionAsync(string baseUri, CancellationToken cancellationToken)
    {
        var response = await GetAsync(baseUri + "/ISAPI/System/deviceInfo", cancellationToken);
        if (response is null)
            return null;

        if (response.Value.StatusCode is 401 or 403)
            return StrongEvidence("Hikvision", 0.95, 80);

        if (!response.Value.IsSuccess)
            return null;

        try
        {
            var xml = XDocument.Parse(response.Value.Body);
            return new ManufacturerDetectionResult
            {
                DetectorName = "LegacyHttp:Hikvision",
                Confidence = 1.0,
                Manufacturer = "Hikvision",
                Model = ReadXml(xml, "model"),
                FirmwareVersion = ReadXml(xml, "firmwareVersion"),
                SerialNumber = ReadXml(xml, "serialNumber"),
                HttpSupported = true,
                HttpPort = ParsePort(baseUri),
                RtspSupported = true,
                RtspPort = 554
            };
        }
        catch
        {
            return StrongEvidence("Hikvision", 0.9, ParsePort(baseUri));
        }
    }

    private static async Task<ManufacturerDetectionResult?> ProbeDahuaAsync(string baseUri, CancellationToken cancellationToken)
    {
        var response = await GetAsync(baseUri + "/cgi-bin/magicBox.cgi?action=getSystemInfo", cancellationToken);
        if (response is null)
            return null;

        if (response.Value.StatusCode is 401 or 403)
            return StrongEvidence("Dahua", 0.95, ParsePort(baseUri));

        if (!response.Value.IsSuccess)
            return null;

        var values = ParseKeyValue(response.Value.Body);
        var model = GetFirst(values, "deviceType", "DeviceType", "model", "Model");
        var firmware = GetFirst(values, "version", "Version", "softwareVersion", "SoftwareVersion");
        var serial = GetFirst(values, "serial", "SerialNo", "serialNumber");

        return new ManufacturerDetectionResult
        {
            DetectorName = "LegacyHttp:Dahua",
            Confidence = 1.0,
            Manufacturer = "Dahua",
            Model = model,
            FirmwareVersion = firmware,
            SerialNumber = serial,
            HttpSupported = true,
            HttpPort = ParsePort(baseUri),
            RtspSupported = true,
            RtspPort = 554
        };
    }

    private static async Task<ManufacturerDetectionResult?> ProbeAxisAsync(string baseUri, CancellationToken cancellationToken)
    {
        var response = await GetAsync(baseUri + "/axis-cgi/param.cgi?action=list&group=Properties.System", cancellationToken);
        if (response is null)
            return null;

        if (response.Value.StatusCode is 401 or 403)
            return StrongEvidence("Axis", 0.95, ParsePort(baseUri));

        if (!response.Value.IsSuccess)
            return null;

        var body = response.Value.Body;
        if (!body.Contains("Properties.System", StringComparison.OrdinalIgnoreCase) &&
            !body.Contains("root.Properties", StringComparison.OrdinalIgnoreCase))
            return null;

        return new ManufacturerDetectionResult
        {
            DetectorName = "LegacyHttp:Axis",
            Confidence = 1.0,
            Manufacturer = "Axis",
            Model = GetAxisValue(body, "ProductFullName") ?? GetAxisValue(body, "ProductName"),
            FirmwareVersion = GetAxisValue(body, "Firmware.Version"),
            SerialNumber = GetAxisValue(body, "System.SerialNumber"),
            HttpSupported = true,
            HttpPort = ParsePort(baseUri),
            RtspSupported = true,
            RtspPort = 554
        };
    }

    private static async Task<ManufacturerDetectionResult?> ProbeVivotekAsync(string baseUri, CancellationToken cancellationToken)
    {
        var response = await GetAsync(baseUri + "/cgi-bin/anonymous/getparam.cgi?system.info", cancellationToken);
        if (response is null)
            return null;

        if (response.Value.StatusCode is 401 or 403)
            return StrongEvidence("VIVOTEK", 0.95, ParsePort(baseUri));

        if (!response.Value.IsSuccess)
        {
            response = await GetAsync(baseUri + "/cgi-bin/sysinfo.cgi", cancellationToken);
            if (response is null || !response.Value.IsSuccess && response.Value.StatusCode is not 401 and not 403)
                return null;
        }

        var values = ParseKeyValue(response.Value.Body);
        var model = GetFirst(values, "system.info_modelname", "modelname", "Model", "model");
        var firmware = GetFirst(values, "system.info_firmwareversion", "firmwareversion", "FirmwareVersion");

        if (string.IsNullOrWhiteSpace(model) && string.IsNullOrWhiteSpace(firmware) &&
            !response.Value.Body.Contains("VIVOTEK", StringComparison.OrdinalIgnoreCase))
            return null;

        return new ManufacturerDetectionResult
        {
            DetectorName = "LegacyHttp:VIVOTEK",
            Confidence = 1.0,
            Manufacturer = "VIVOTEK",
            Model = model,
            FirmwareVersion = firmware,
            HttpSupported = true,
            HttpPort = ParsePort(baseUri),
            RtspSupported = true,
            RtspPort = 554
        };
    }

    private static async Task<ManufacturerDetectionResult?> ProbeReolinkAsync(string baseUri, CancellationToken cancellationToken)
    {
        var response = await GetAsync(baseUri + "/cgi-bin/api.cgi?cmd=GetDevInfo&channel=0&rs=value", cancellationToken);
        if (response is null)
            return null;

        if (response.Value.StatusCode is 401 or 403)
            return StrongEvidence("Reolink", 0.9, ParsePort(baseUri));

        if (!response.Value.IsSuccess || string.IsNullOrWhiteSpace(response.Value.Body))
            return null;

        try
        {
            using var json = JsonDocument.Parse(response.Value.Body);
            var root = json.RootElement;
            var info = root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0
                ? root[0].GetProperty("value")
                : root;

            return new ManufacturerDetectionResult
            {
                DetectorName = "LegacyHttp:Reolink",
                Confidence = 0.95,
                Manufacturer = "Reolink",
                Model = TryJson(info, "DevInfo", "type") ?? TryJson(info, "type"),
                FirmwareVersion = TryJson(info, "firmVer"),
                SerialNumber = TryJson(info, "serialNumber"),
                HttpSupported = true,
                HttpPort = ParsePort(baseUri),
                RtspSupported = true,
                RtspPort = 554
            };
        }
        catch
        {
            return null;
        }
    }

    private static async Task<ProbeResponse?> GetAsync(string uri, CancellationToken cancellationToken)
    {
        try
        {
            using var handler = new HttpClientHandler { AllowAutoRedirect = false };
            using var client = new HttpClient(handler) { Timeout = Timeout };
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/plain"));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/xml"));

            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            return new ProbeResponse((int)response.StatusCode, response.IsSuccessStatusCode, body);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    private static ManufacturerDetectionResult StrongEvidence(string manufacturer, double confidence, int? port) => new()
    {
        DetectorName = "LegacyHttp:" + manufacturer,
        Confidence = confidence,
        Manufacturer = manufacturer,
        HttpSupported = true,
        HttpPort = port
    };

    private static string? ReadXml(XDocument document, string localName) =>
        document.Descendants().FirstOrDefault(e => e.Name.LocalName.Equals(localName, StringComparison.OrdinalIgnoreCase))?.Value.Trim();

    private static Dictionary<string, string> ParseKeyValue(string body)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in body.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var index = line.IndexOf('=');
            if (index <= 0) continue;
            var key = line[..index].Trim();
            var value = line[(index + 1)..].Trim();
            if (key.Length > 0) values[key] = value;
        }
        return values;
    }

    private static string? GetFirst(IReadOnlyDictionary<string, string> values, params string[] keys)
    {
        foreach (var key in keys)
            if (values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)) return value;
        return null;
    }

    private static string? GetAxisValue(string body, string key)
    {
        foreach (var line in body.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var index = line.IndexOf('=');
            if (index <= 0) continue;
            var left = line[..index].Trim();
            if (left.EndsWith(key, StringComparison.OrdinalIgnoreCase))
                return line[(index + 1)..].Trim().Trim('"');
        }
        return null;
    }

    private static string? TryJson(JsonElement element, params string[] paths)
    {
        foreach (var path in paths)
        {
            var current = element;
            var ok = true;
            foreach (var part in path.Split('.'))
            {
                if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(part, out current))
                {
                    ok = false;
                    break;
                }
            }
            if (ok && current.ValueKind == JsonValueKind.String)
                return current.GetString();
        }
        return null;
    }

    private static int? ParsePort(string uri)
        => Uri.TryCreate(uri, UriKind.Absolute, out var parsed) && parsed.Port > 0 ? parsed.Port : null;

    private sealed record ProbeResponse(int StatusCode, bool IsSuccess, string Body);
}
