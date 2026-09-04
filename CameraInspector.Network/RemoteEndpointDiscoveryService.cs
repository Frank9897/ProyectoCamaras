using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;
using CameraInspector.Core.Interfaces;
using CameraInspector.Core.Models;

namespace CameraInspector.Network;

/// <summary>
/// Prueba un punto de entrada remoto por host + puerto y busca evidencias de un servicio de cámara.
/// No presupone VAST, VSS ni otro fabricante: el protocolo se identifica durante la conexión.
/// </summary>
public sealed class RemoteEndpointDiscoveryService : IRemoteCameraDiscoveryService
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromMilliseconds(900);
    private static readonly TimeSpan TlsTimeout = TimeSpan.FromMilliseconds(1500);
    private const int ProbeBytes = 16 * 1024;

    public async Task<RemoteConnectionResult> ProbeAsync(
        RemoteConnectionTarget target,
        CancellationToken cancellationToken = default)
    {
        ValidateTarget(target);

        try
        {
            await ConnectAndDisposeAsync(target, cancellationToken);
        }
        catch (Exception ex)
        {
            return new RemoteConnectionResult(
                false,
                "TCP",
                $"No se pudo establecer el enlace con {target.Host}:{target.Port}: {ex.Message}");
        }

        var protocol = target.Protocol.Equals("AUTO", StringComparison.OrdinalIgnoreCase)
            ? await DetectProtocolAsync(target, cancellationToken)
            : target.Protocol.Trim().ToUpperInvariant();

        var message = protocol switch
        {
            "HTTP" => $"Enlace TCP activo en {target.Host}:{target.Port}. Se detectó HTTP.",
            "HTTPS" => $"Enlace TCP activo en {target.Host}:{target.Port}. Se detectó HTTPS/TLS.",
            "RTSP" => $"Enlace TCP activo en {target.Host}:{target.Port}. Se detectó RTSP.",
            _ => $"Enlace TCP activo en {target.Host}:{target.Port}. No se identificó un protocolo de aplicación conocido."
        };

        return new RemoteConnectionResult(true, protocol, message);
    }

    public async Task<IReadOnlyList<DiscoveredDevice>> DiscoverAsync(
        RemoteConnectionTarget target,
        CancellationToken cancellationToken = default)
    {
        ValidateTarget(target);

        try
        {
            await using var client = new AsyncDisposableTcpClient(await ConnectAsync(target, cancellationToken));

            var protocol = target.Protocol.Equals("AUTO", StringComparison.OrdinalIgnoreCase)
                ? await DetectProtocolAsync(target, cancellationToken)
                : target.Protocol.Trim().ToUpperInvariant();

            var ip = await ResolveIPv4Async(target.Host, cancellationToken);
            if (ip is null)
                return Array.Empty<DiscoveredDevice>();

            var device = new DiscoveredDevice
            {
                IpAddress = ip,
                Status = DeviceStatus.Online,
                HttpSupported = protocol is "HTTP" or "HTTPS",
                HttpsSupported = protocol == "HTTPS",
                RtspSupported = protocol == "RTSP"
            };

            if (device.HttpSupported)
                device.HttpPort = target.Port;
            if (device.RtspSupported)
                device.RtspPort = target.Port;

            var fingerprint = await ProbeApplicationFingerprintAsync(target, protocol, cancellationToken);
            if (!string.IsNullOrWhiteSpace(fingerprint.Server))
            {
                device.Manufacturer = fingerprint.Manufacturer;
                device.Model = fingerprint.Model;
                device.Hostname = fingerprint.Server;
            }

            var cameraEvidence = fingerprint.IsCamera;
            device.CameraEvidence = cameraEvidence;
            device.AddEvidence(
                cameraEvidence ? "RemoteCameraFingerprint" : "RemoteEndpoint",
                cameraEvidence ? 0.92 : 0.28,
                $"endpoint remoto {target.Host}:{target.Port} · protocolo {protocol}",
                cameraEvidence);

            return new[] { device };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return Array.Empty<DiscoveredDevice>();
        }
    }

    private static async Task<TcpClient> ConnectAsync(
        RemoteConnectionTarget target,
        CancellationToken cancellationToken)
    {
        var client = new TcpClient();
        try
        {
            await ConnectAsync(client, target.Host, target.Port, cancellationToken);
            return client;
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    private static async Task ConnectAndDisposeAsync(
        RemoteConnectionTarget target,
        CancellationToken cancellationToken)
    {
        using var client = await ConnectAsync(target, cancellationToken);
    }

    private static async Task ConnectAsync(
        TcpClient client,
        string host,
        int port,
        CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(ConnectTimeout);
        await client.ConnectAsync(host, port, timeoutCts.Token);
    }

    private static async Task<string> DetectProtocolAsync(
        RemoteConnectionTarget target,
        CancellationToken cancellationToken)
    {
        if (target.Port is 443 or 8443)
        {
            if (await TryHttpsProbeAsync(target, cancellationToken))
                return "HTTPS";

            var http = await TryHttpProbeAsync(target, cancellationToken);
            if (http is not null)
                return "HTTP";

            return await TryRtspProbeAsync(target, cancellationToken) ? "RTSP" : "TCP";
        }

        if (target.Port is 554 or 8554)
        {
            if (await TryRtspProbeAsync(target, cancellationToken))
                return "RTSP";

            var http = await TryHttpProbeAsync(target, cancellationToken);
            if (http is not null)
                return "HTTP";

            return await TryHttpsProbeAsync(target, cancellationToken) ? "HTTPS" : "TCP";
        }

        var defaultHttp = await TryHttpProbeAsync(target, cancellationToken);
        if (defaultHttp is not null)
            return "HTTP";

        if (await TryHttpsProbeAsync(target, cancellationToken))
            return "HTTPS";

        return await TryRtspProbeAsync(target, cancellationToken) ? "RTSP" : "TCP";
    }

    private static async Task<bool> TryHttpsProbeAsync(
        RemoteConnectionTarget target,
        CancellationToken cancellationToken)
    {
        try
        {
            using var client = await ConnectAsync(target, cancellationToken);
            using var stream = client.GetStream();
            using var ssl = new SslStream(stream, leaveInnerStreamOpen: false, (_, _, _, _) => true);
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TlsTimeout);

            await ssl.AuthenticateAsClientAsync(
                new SslClientAuthenticationOptions
                {
                    TargetHost = target.Host,
                    EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                    CertificateRevocationCheckMode = System.Security.Cryptography.X509Certificates.X509RevocationMode.NoCheck,
                    RemoteCertificateValidationCallback = (_, _, _, _) => true
                },
                timeoutCts.Token);

            return ssl.IsAuthenticated;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<HttpProbeResult?> TryHttpProbeAsync(
        RemoteConnectionTarget target,
        CancellationToken cancellationToken)
    {
        try
        {
            using var client = await ConnectAsync(target, cancellationToken);
            using var stream = client.GetStream();
            var request = Encoding.ASCII.GetBytes(
                $"HEAD / HTTP/1.1\r\nHost: {target.Host}\r\nConnection: close\r\nUser-Agent: CameraInspector/1.0\r\n\r\n");
            await stream.WriteAsync(request, cancellationToken);
            await stream.FlushAsync(cancellationToken);

            var buffer = new byte[ProbeBytes];
            var count = await stream.ReadAsync(buffer, cancellationToken);
            if (count <= 0)
                return null;

            var text = Encoding.ASCII.GetString(buffer, 0, count);
            if (!text.StartsWith("HTTP/", StringComparison.OrdinalIgnoreCase))
                return null;

            return new HttpProbeResult(text);
        }
        catch
        {
            return null;
        }
    }

    private static async Task<bool> TryRtspProbeAsync(
        RemoteConnectionTarget target,
        CancellationToken cancellationToken)
    {
        try
        {
            using var client = await ConnectAsync(target, cancellationToken);
            using var stream = client.GetStream();
            var request = Encoding.ASCII.GetBytes(
                $"OPTIONS rtsp://{target.Host}:{target.Port}/ RTSP/1.0\r\nCSeq: 1\r\nUser-Agent: CameraInspector/1.0\r\n\r\n");
            await stream.WriteAsync(request, cancellationToken);
            await stream.FlushAsync(cancellationToken);
            var buffer = new byte[4096];
            var count = await stream.ReadAsync(buffer, cancellationToken);
            if (count <= 0)
                return false;
            var text = Encoding.ASCII.GetString(buffer, 0, count);
            return text.StartsWith("RTSP/", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static async Task<(bool IsCamera, string? Manufacturer, string? Model, string? Server)> ProbeApplicationFingerprintAsync(
        RemoteConnectionTarget target,
        string protocol,
        CancellationToken cancellationToken)
    {
        if (protocol is "HTTP" or "HTTPS")
        {
            var http = await ReadHttpFingerprintAsync(target, protocol, cancellationToken);
            if (http is null)
                return default;

            var server = http.Value.Server;
            var body = http.Value.Body;
            var combined = $"{server}\n{body}";
            var camera = ContainsCameraToken(combined);
            var manufacturer = DetectManufacturer(combined);
            return (camera, manufacturer, DetectModel(body), server);
        }

        if (protocol == "RTSP")
        {
            var response = await ReadRtspOptionsAsync(target, cancellationToken);
            var camera = ContainsCameraToken(response);
            var manufacturer = DetectManufacturer(response);
            return (camera, manufacturer, null, ExtractServerHeader(response));
        }

        return default;
    }

    private static async Task<(string Server, string Body)?> ReadHttpFingerprintAsync(
        RemoteConnectionTarget target,
        string protocol,
        CancellationToken cancellationToken)
    {
        try
        {
            using var client = await ConnectAsync(target, cancellationToken);
            if (protocol == "HTTPS")
            {
                using var innerStream = client.GetStream();
                using var stream = new SslStream(innerStream, leaveInnerStreamOpen: false, (_, _, _, _) => true);
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(TlsTimeout);
                await stream.AuthenticateAsClientAsync(
                    new SslClientAuthenticationOptions
                    {
                        TargetHost = target.Host,
                        EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                        CertificateRevocationCheckMode = System.Security.Cryptography.X509Certificates.X509RevocationMode.NoCheck,
                        RemoteCertificateValidationCallback = (_, _, _, _) => true
                    },
                    timeoutCts.Token);
                return await ReadHttpResponseAsync(stream, target, cancellationToken);
            }

            return await ReadHttpResponseAsync(client.GetStream(), target, cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    private static async Task<(string Server, string Body)?> ReadHttpResponseAsync(
        Stream stream,
        RemoteConnectionTarget target,
        CancellationToken cancellationToken)
    {
        var request = Encoding.ASCII.GetBytes(
            $"GET / HTTP/1.1\r\nHost: {target.Host}\r\nConnection: close\r\nUser-Agent: CameraInspector/1.0\r\n\r\n");
        await stream.WriteAsync(request, cancellationToken);
        await stream.FlushAsync(cancellationToken);

        var buffer = new byte[ProbeBytes];
        var count = await stream.ReadAsync(buffer, cancellationToken);
        if (count <= 0)
            return null;

        var text = Encoding.UTF8.GetString(buffer, 0, count);
        var separator = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        var headers = separator >= 0 ? text[..separator] : text;
        var body = separator >= 0 ? text[(separator + 4)..] : string.Empty;
        var server = ExtractServerHeader(headers) ?? string.Empty;
        return (server, body);
    }

    private static async Task<string> ReadRtspOptionsAsync(
        RemoteConnectionTarget target,
        CancellationToken cancellationToken)
    {
        try
        {
            using var client = await ConnectAsync(target, cancellationToken);
            using var stream = client.GetStream();
            var request = Encoding.ASCII.GetBytes(
                $"OPTIONS rtsp://{target.Host}:{target.Port}/ RTSP/1.0\r\nCSeq: 1\r\nUser-Agent: CameraInspector/1.0\r\n\r\n");
            await stream.WriteAsync(request, cancellationToken);
            await stream.FlushAsync(cancellationToken);
            var buffer = new byte[ProbeBytes];
            var count = await stream.ReadAsync(buffer, cancellationToken);
            return count <= 0 ? string.Empty : Encoding.ASCII.GetString(buffer, 0, count);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static async Task<string?> ResolveIPv4Async(string host, CancellationToken cancellationToken)
    {
        if (IPAddress.TryParse(host, out var parsed))
            return parsed.AddressFamily == AddressFamily.InterNetwork ? parsed.ToString() : null;

        try
        {
            var addresses = await Dns.GetHostAddressesAsync(host, cancellationToken);
            return addresses.FirstOrDefault(address => address.AddressFamily == AddressFamily.InterNetwork)?.ToString();
        }
        catch
        {
            return null;
        }
    }

    private static bool ContainsCameraToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var tokens = new[]
        {
            "vivotek", "hikvision", "dahua", "axis", "hanwha", "samsung", "uniview",
            "reolink", "mobotix", "camera", "ipcam", "ip camera", "network camera",
            "onvif"
        };
        return tokens.Any(token => value.Contains(token, StringComparison.OrdinalIgnoreCase));
    }

    private static string? DetectManufacturer(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var manufacturers = new[] { "VIVOTEK", "HIKVISION", "DAHUA", "AXIS", "HANWHA", "SAMSUNG", "UNIVIEW", "REOLINK", "MOBOTIX" };
        return manufacturers.FirstOrDefault(item => value.Contains(item, StringComparison.OrdinalIgnoreCase));
    }

    private static string? DetectModel(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var markers = new[] { "model", "product", "device" };
        foreach (var marker in markers)
        {
            var index = value.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (index < 0) continue;
            var line = value[index..].Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(line))
                return line.Trim().Trim(' ', ':', '=', '\"');
        }
        return null;
    }

    private static string? ExtractServerHeader(string? headers)
    {
        if (string.IsNullOrWhiteSpace(headers)) return null;
        foreach (var line in headers.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.StartsWith("Server:", StringComparison.OrdinalIgnoreCase))
                return line[7..].Trim();
        }
        return null;
    }

    private static void ValidateTarget(RemoteConnectionTarget target)
    {
        if (target is null || string.IsNullOrWhiteSpace(target.Host))
            throw new ArgumentException("Debe indicar un host remoto.", nameof(target));
        if (target.Port is < 1 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(target.Port), "El puerto debe estar entre 1 y 65535.");
    }

    private readonly record struct HttpProbeResult(string Headers);

    private sealed class AsyncDisposableTcpClient : IAsyncDisposable
    {
        private readonly TcpClient _client;

        public AsyncDisposableTcpClient(TcpClient client) => _client = client;

        public ValueTask DisposeAsync()
        {
            _client.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
