using System.Net.Http.Headers;
using System.Text.Json;
using System.Xml.Linq;
using CameraInspector.Core.Interfaces;
using CameraInspector.Core.Models;

namespace CameraInspector.Network.Detection;

/// <summary>
/// Detector HTTP para cámaras propietarias o antiguas sin ONVIF.
/// Una respuesta 401/403 en un endpoint específico también constituye evidencia fuerte del fabricante.
/// </summary>
public sealed class LegacyCameraHttpDetector : IManufacturerDetector
{
    private static readonly int[] DefaultHttpPorts = { 80, 81, 82, 88, 443, 8080, 8081, 8443 };
    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(1100);

    public string Name => "LegacyHttp";

    public async Task<ManufacturerDetectionResult?> TryDetectAsync(
        DiscoveredDevice device,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(device.IpAddress))
            return null;

        var ports = new List<int>();
        if (device.HttpPort is int configured)
            ports.Add(configured);
        ports.AddRange(DefaultHttpPorts);

        foreach (var port in ports.Distinct())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var schemes = port is 443 or 8443 ? new[] { "https" } : new[] { "http" };

            foreach (var scheme in schemes)
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
                result = await ProbeHanwhaAsync(baseUri, cancellationToken);
                if (result is not null) return result;
                result = await ProbeUniviewAsync(baseUri, cancellationToken);
                if (result is not null) return result;
                result = await ProbeMobotixAsync(baseUri, cancellationToken);
                if (result is not null) return result;
            }
        }

        return null;
    }

    private static async Task<ManufacturerDetectionResult?> ProbeHikvisionAsync(string baseUri, CancellationToken cancellationToken)
    {
        var response = await GetAsync(baseUri + "/ISAPI/System/deviceInfo", cancellationToken);
        if (response is null) return null;
        if (response.StatusCode is 401 or 403) return StrongEvidence("Hikvision", 0.95, ParsePort(baseUri));
        if (!response.IsSuccess) return null;
        try
        {
            var xml = XDocument.Parse(response.Body);
            return new ManufacturerDetectionResult
            {
                DetectorName = "LegacyHttp:Hikvision", Confidence = 1.0, Manufacturer = "Hikvision",
                Model = ReadXml(xml, "model"), FirmwareVersion = ReadXml(xml, "firmwareVersion"),
                SerialNumber = ReadXml(xml, "serialNumber"), HttpSupported = true,
                HttpPort = ParsePort(baseUri), RtspSupported = true, RtspPort = 554
            };
        }
        catch { return StrongEvidence("Hikvision", 0.9, ParsePort(baseUri)); }
    }

    private static async Task<ManufacturerDetectionResult?> ProbeDahuaAsync(string baseUri, CancellationToken cancellationToken)
    {
        var response = await GetAsync(baseUri + "/cgi-bin/magicBox.cgi?action=getSystemInfo", cancellationToken);
        if (response is null) return null;
        if (response.StatusCode is 401 or 403) return StrongEvidence("Dahua", 0.95, ParsePort(baseUri));
        if (!response.IsSuccess) return null;
        var values = ParseKeyValue(response.Body);
        return new ManufacturerDetectionResult
        {
            DetectorName = "LegacyHttp:Dahua", Confidence = 1.0, Manufacturer = "Dahua",
            Model = GetFirst(values, "deviceType", "DeviceType", "model", "Model"),
            FirmwareVersion = GetFirst(values, "version", "Version", "softwareVersion", "SoftwareVersion"),
            SerialNumber = GetFirst(values, "serial", "SerialNo", "serialNumber"),
            HttpSupported = true, HttpPort = ParsePort(baseUri), RtspSupported = true, RtspPort = 554
        };
    }

    private static async Task<ManufacturerDetectionResult?> ProbeAxisAsync(string baseUri, CancellationToken cancellationToken)
    {
        var response = await GetAsync(baseUri + "/axis-cgi/param.cgi?action=list&group=Properties.System", cancellationToken);
        if (response is null) return null;
        if (response.StatusCode is 401 or 403) return StrongEvidence("Axis", 0.95, ParsePort(baseUri));
        if (!response.IsSuccess) return null;
        var body = response.Body;
        if (!body.Contains("Properties.System", StringComparison.OrdinalIgnoreCase) &&
            !body.Contains("root.Properties", StringComparison.OrdinalIgnoreCase) &&
            !body.Contains("Axis", StringComparison.OrdinalIgnoreCase)) return null;
        return new ManufacturerDetectionResult
        {
            DetectorName = "LegacyHttp:Axis", Confidence = 1.0, Manufacturer = "Axis",
            Model = GetAxisValue(body, "ProductFullName") ?? GetAxisValue(body, "ProductName"),
            FirmwareVersion = GetAxisValue(body, "Firmware.Version"), SerialNumber = GetAxisValue(body, "System.SerialNumber"),
            HttpSupported = true, HttpPort = ParsePort(baseUri), RtspSupported = true, RtspPort = 554
        };
    }

    private static async Task<ManufacturerDetectionResult?> ProbeVivotekAsync(string baseUri, CancellationToken cancellationToken)
    {
        var response = await GetAsync(baseUri + "/cgi-bin/anonymous/getparam.cgi?system.info", cancellationToken);
        if (response is null) return null;
        if (response.StatusCode is 401 or 403) return StrongEvidence("VIVOTEK", 0.98, ParsePort(baseUri));
        if (!response.IsSuccess)
        {
            response = await GetAsync(baseUri + "/cgi-bin/sysinfo.cgi", cancellationToken);
            if (response is null) return null;
            if (response.StatusCode is 401 or 403) return StrongEvidence("VIVOTEK", 0.98, ParsePort(baseUri));
            if (!response.IsSuccess) return null;
        }
        var values = ParseKeyValue(response.Body);
        var model = GetFirst(values, "system.info_modelname", "modelname", "Model", "model");
        var firmware = GetFirst(values, "system.info_firmwareversion", "firmwareversion", "FirmwareVersion");
        if (string.IsNullOrWhiteSpace(model) && string.IsNullOrWhiteSpace(firmware) &&
            !response.Body.Contains("VIVOTEK", StringComparison.OrdinalIgnoreCase)) return null;
        return new ManufacturerDetectionResult
        {
            DetectorName = "LegacyHttp:VIVOTEK", Confidence = 1.0, Manufacturer = "VIVOTEK", Model = model,
            FirmwareVersion = firmware, HttpSupported = true, HttpPort = ParsePort(baseUri), RtspSupported = true, RtspPort = 554
        };
    }

    private static async Task<ManufacturerDetectionResult?> ProbeReolinkAsync(string baseUri, CancellationToken cancellationToken)
    {
        var response = await GetAsync(baseUri + "/cgi-bin/api.cgi?cmd=GetDevInfo&channel=0&rs=value", cancellationToken);
        if (response is null) return null;
        if (response.StatusCode is 401 or 403) return StrongEvidence("Reolink", 0.9, ParsePort(baseUri));
        if (!response.IsSuccess || string.IsNullOrWhiteSpace(response.Body)) return null;
        try
        {
            using var json = JsonDocument.Parse(response.Body);
            var root = json.RootElement;
            var info = root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0 && root[0].TryGetProperty("value", out var value) ? value : root;
            return new ManufacturerDetectionResult
            {
                DetectorName = "LegacyHttp:Reolink", Confidence = 0.95, Manufacturer = "Reolink",
                Model = TryJson(info, "DevInfo.type", "type"), FirmwareVersion = TryJson(info, "firmVer"),
                SerialNumber = TryJson(info, "serialNumber"), HttpSupported = true, HttpPort = ParsePort(baseUri),
                RtspSupported = true, RtspPort = 554
            };
        }
        catch { return null; }
    }

    private static async Task<ManufacturerDetectionResult?> ProbeHanwhaAsync(string baseUri, CancellationToken cancellationToken)
    {
        var response = await GetAsync(baseUri + "/stw-cgi/system.cgi?msubmenu=deviceinfo&action=view", cancellationToken);
        if (response is null) return null;
        if (response.StatusCode is 401 or 403) return StrongEvidence("Hanwha", 0.95, ParsePort(baseUri));
        if (!response.IsSuccess) return null;
        var body = response.Body;
        if (!body.Contains("Model=", StringComparison.OrdinalIgnoreCase) &&
            !body.Contains("FirmwareVersion=", StringComparison.OrdinalIgnoreCase) &&
            !body.Contains("SUNAPI", StringComparison.OrdinalIgnoreCase)) return null;
        var values = ParseKeyValue(body);
        return new ManufacturerDetectionResult
        {
            DetectorName = "LegacyHttp:Hanwha", Confidence = 1.0, Manufacturer = "Hanwha",
            Model = GetFirst(values, "Model", "model"), FirmwareVersion = GetFirst(values, "FirmwareVersion", "firmwareVersion"),
            SerialNumber = GetFirst(values, "SerialNumber", "serialNumber"), HttpSupported = true,
            HttpPort = ParsePort(baseUri), RtspSupported = true, RtspPort = 554
        };
    }

    private static async Task<ManufacturerDetectionResult?> ProbeUniviewAsync(string baseUri, CancellationToken cancellationToken)
    {
        var response = await GetAsync(baseUri + "/LAPI/V1.0/System/DeviceBasicInfo", cancellationToken);
        if (response is null) return null;
        if (response.StatusCode is 401 or 403) return StrongEvidence("Uniview", 0.95, ParsePort(baseUri));
        if (!response.IsSuccess) return null;
        if (!response.Body.Contains("Device", StringComparison.OrdinalIgnoreCase) &&
            !response.Body.Contains("Model", StringComparison.OrdinalIgnoreCase) &&
            !response.Body.Contains("UNV", StringComparison.OrdinalIgnoreCase)) return null;
        return new ManufacturerDetectionResult
        {
            DetectorName = "LegacyHttp:Uniview", Confidence = 0.95, Manufacturer = "Uniview",
            Model = TryExtractLoose(response.Body, "Model", "model"),
            FirmwareVersion = TryExtractLoose(response.Body, "FirmwareVersion", "firmware"),
            HttpSupported = true, HttpPort = ParsePort(baseUri), RtspSupported = true, RtspPort = 554
        };
    }

    private static async Task<ManufacturerDetectionResult?> ProbeMobotixAsync(string baseUri, CancellationToken cancellationToken)
    {
        var response = await GetAsync(baseUri + "/control/control?list", cancellationToken);
        if (response is null) return null;
        if (response.StatusCode is 401 or 403) return StrongEvidence("MOBOTIX", 0.95, ParsePort(baseUri));
        if (!response.IsSuccess) return null;
        var body = response.Body;
        if (!body.Contains("MOBOTIX", StringComparison.OrdinalIgnoreCase) &&
            !body.Contains("recording", StringComparison.OrdinalIgnoreCase) &&
            !body.Contains("vptz", StringComparison.OrdinalIgnoreCase)) return null;
        return new ManufacturerDetectionResult
        {
            DetectorName = "LegacyHttp:MOBOTIX", Confidence = 0.9, Manufacturer = "MOBOTIX",
            HttpSupported = true, HttpPort = ParsePort(baseUri), RtspSupported = true, RtspPort = 554
        };
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
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return null; }
        catch (HttpRequestException) { return null; }
    }

    private static ManufacturerDetectionResult StrongEvidence(string manufacturer, double confidence, int? port) => new()
    {
        DetectorName = "LegacyHttp:" + manufacturer, Confidence = confidence, Manufacturer = manufacturer,
        HttpSupported = true, HttpPort = port, RtspSupported = true, RtspPort = 554
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
            values[line[..index].Trim()] = line[(index + 1)..].Trim().Trim('"');
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
            if (line[..index].Trim().EndsWith(key, StringComparison.OrdinalIgnoreCase))
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
                if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(part, out current)) { ok = false; break; }
            }
            if (ok && current.ValueKind == JsonValueKind.String) return current.GetString();
        }
        return null;
    }

    private static string? TryExtractLoose(string body, params string[] names)
    {
        foreach (var name in names)
        {
            var marker = "\"" + name + "\"";
            var index = body.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (index < 0) continue;
            var colon = body.IndexOf(':', index + marker.Length);
            if (colon < 0) continue;
            var start = body.IndexOf('"', colon + 1);
            if (start < 0) continue;
            var end = body.IndexOf('"', start + 1);
            if (end > start) return body[(start + 1)..end];
        }
        return null;
    }

    private static int? ParsePort(string uri) =>
        Uri.TryCreate(uri, UriKind.Absolute, out var parsed) && parsed.Port > 0 ? parsed.Port : null;

    private sealed record ProbeResponse(int StatusCode, bool IsSuccess, string Body);
}
