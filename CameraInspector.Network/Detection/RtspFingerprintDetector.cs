using System.Net.Sockets;
using System.Text;
using CameraInspector.Core.Interfaces;
using CameraInspector.Core.Models;

namespace CameraInspector.Network.Detection;

/// <summary>
/// Fingerprint mínimo de un servidor RTSP. Usa OPTIONS, que no cambia el estado del servidor,
/// para confirmar RTSP y recuperar la cabecera Server/Public cuando el equipo la expone.
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
        if (device.RtspPort is int known)
            ports.Add(known);
        ports.AddRange(DefaultRtspPorts);

        foreach (var port in ports.Distinct())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await ProbeAsync(device.IpAddress, port, cancellationToken);
            if (result is null)
                continue;

            var response = result.Value;
            if (!response.StartsWith("RTSP/1.0", StringComparison.OrdinalIgnoreCase))
                continue;

            var server = GetHeader(response, "Server");
            var publicMethods = GetHeader(response, "Public");
            var manufacturer = DetectManufacturer(server);

            return new ManufacturerDetectionResult
            {
                DetectorName = Name,
                Confidence = manufacturer is null ? 0.75 : 0.9,
                Manufacturer = manufacturer,
                RtspSupported = true,
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
                "OPTIONS * RTSP/1.0\r\n" +
                "CSeq: 1\r\n" +
                "User-Agent: CameraInspector/1.0\r\n\r\n");

            await stream.WriteAsync(request, timeout.Token);
            var buffer = new byte[4096];
            var bytesRead = await stream.ReadAsync(buffer, timeout.Token);
            return bytesRead > 0 ? Encoding.ASCII.GetString(buffer, 0, bytesRead) : null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (SocketException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static string? GetHeader(string response, string name)
    {
        foreach (var line in response.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var index = line.IndexOf(':');
            if (index <= 0)
                continue;

            if (line[..index].Trim().Equals(name, StringComparison.OrdinalIgnoreCase))
                return line[(index + 1)..].Trim();
        }
        return null;
    }

    private static string? DetectManufacturer(string? server)
    {
        if (string.IsNullOrWhiteSpace(server))
            return null;

        var signatures = new (string Needle, string Manufacturer)[]
        {
            ("vivotek", "VIVOTEK"),
            ("hikvision", "Hikvision"),
            ("dahua", "Dahua"),
            ("axis", "Axis"),
            ("hanwha", "Hanwha"),
            ("wisenet", "Hanwha"),
            ("uniview", "Uniview"),
            ("mobotix", "MOBOTIX"),
            ("reolink", "Reolink")
        };

        return signatures.FirstOrDefault(item =>
            server.Contains(item.Needle, StringComparison.OrdinalIgnoreCase)).Manufacturer;
    }
}
