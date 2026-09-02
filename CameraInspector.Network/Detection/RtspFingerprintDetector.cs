using System.Net.Sockets;
using System.Text;
using CameraInspector.Core.Interfaces;
using CameraInspector.Core.Models;

namespace CameraInspector.Network.Detection;

/// <summary>
/// Fingerprint de un servidor RTSP. Una respuesta RTSP por sí sola no identifica una cámara:
/// solo se clasifica como cámara cuando existe una firma de fabricante conocida.
/// </summary>
public sealed class RtspFingerprintDetector : IManufacturerDetector
{
    private static readonly int[] DefaultRtspPorts = { 554, 8554, 10554 };
    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(900);

    public string Name => "RtspFingerprint";

    public async Task<ManufacturerDetectionResult?> TryDetectAsync(
        DiscoveredDevice device,
        CancellationToken cancellationToken = default)
    {
        var ports = new List<int>();
        if (device.RtspPort is int known) ports.Add(known);
        ports.AddRange(DefaultRtspPorts);

        foreach (var port in ports.Distinct())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var response = await ProbeAsync(device.IpAddress, port, cancellationToken);
            if (response is null || !response.StartsWith("RTSP/1.0", StringComparison.OrdinalIgnoreCase))
                continue;

            var server = GetHeader(response, "Server");
            var manufacturer = DetectManufacturer(server);
            var isKnownCameraServer = !string.IsNullOrWhiteSpace(manufacturer);

            return new ManufacturerDetectionResult
            {
                DetectorName = Name,
                Confidence = isKnownCameraServer ? 0.92 : 0.38,
                CameraEvidence = isKnownCameraServer,
                EvidenceDetails = string.IsNullOrWhiteSpace(server)
                    ? $"RTSP detectado en TCP/{port}; sin firma de cámara"
                    : isKnownCameraServer
                        ? $"RTSP/1.0 en TCP/{port} · Server: {server}"
                        : $"RTSP detectado en TCP/{port} · Server genérico: {server}",
                Manufacturer = manufacturer,
                RtspSupported = isKnownCameraServer,
                RtspPort = port,
                HttpSupported = device.HttpSupported
            };
        }

        return null;
    }

    private static async Task<string?> ProbeAsync(string ip, int port, CancellationToken cancellationToken)
    {
        using var client = new TcpClient();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);
        try
        {
            await client.ConnectAsync(ip, port, timeout.Token);
            await using var stream = client.GetStream();
            var request = Encoding.ASCII.GetBytes(
                "OPTIONS * RTSP/1.0\r\nCSeq: 1\r\nUser-Agent: CameraInspector/1.0\r\n\r\n");
            await stream.WriteAsync(request, timeout.Token);
            var buffer = new byte[4096];
            var bytesRead = await stream.ReadAsync(buffer, timeout.Token);
            return bytesRead > 0 ? Encoding.ASCII.GetString(buffer, 0, bytesRead) : null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return null; }
        catch (SocketException) { return null; }
        catch (IOException) { return null; }
    }

    private static string? GetHeader(string response, string name)
    {
        foreach (var line in response.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var index = line.IndexOf(':');
            if (index <= 0) continue;
            if (line[..index].Trim().Equals(name, StringComparison.OrdinalIgnoreCase))
                return line[(index + 1)..].Trim();
        }
        return null;
    }

    private static string? DetectManufacturer(string? server)
    {
        if (string.IsNullOrWhiteSpace(server)) return null;
        var signatures = new (string Needle, string Manufacturer)[]
        {
            ("vivotek", "VIVOTEK"), ("hikvision", "Hikvision"), ("dahua", "Dahua"),
            ("axis", "Axis"), ("hanwha", "Hanwha"), ("wisenet", "Hanwha"),
            ("uniview", "Uniview"), ("mobotix", "MOBOTIX"), ("reolink", "Reolink")
        };
        return signatures.FirstOrDefault(item => server.Contains(item.Needle, StringComparison.OrdinalIgnoreCase)).Manufacturer;
    }
}
