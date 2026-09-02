using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using CameraInspector.Core.Interfaces;
using CameraInspector.Core.Models;

namespace CameraInspector.Network.Diagnostics;

/// <summary>
/// Comprobación ligera para diferenciar sin respuesta, comunicación sin vídeo,
/// vídeo disponible y autenticación requerida.
/// </summary>
public sealed class CameraHealthService : ICameraHealthService
{
    private static readonly int[] CommonPorts = { 80, 443, 554, 8000, 8080, 8554, 37777, 9000 };
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromMilliseconds(450);
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromMilliseconds(700);
    private static readonly HttpClient Http = new(new HttpClientHandler { AllowAutoRedirect = false })
    {
        Timeout = TimeSpan.FromMilliseconds(800)
    };

    private static readonly string[] SnapshotPaths =
    {
        "/snapshot.jpg",
        "/snap.jpg",
        "/image.jpg",
        "/snapshot.cgi",
        "/cgi-bin/snapshot.cgi"
    };

    public async Task<CameraHealthResult> CheckAsync(
        DiscoveredDevice device,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(device.IpAddress))
        {
            return Result(CameraHealthState.NoResponse, false, false, false,
                null, null, "La cámara no tiene una dirección IP válida.");
        }

        var ports = await FindOpenPortsAsync(device.IpAddress, cancellationToken);
        if (ports.Count == 0)
        {
            return Result(CameraHealthState.NoResponse, false, false, false,
                null, null, "Sin respuesta: no se pudo establecer comunicación TCP con puertos habituales de cámara.");
        }

        foreach (var port in ports)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (port is 554 or 8554)
            {
                var rtsp = await ProbeRtspAsync(device.IpAddress, port, cancellationToken);
                if (rtsp.VideoAvailable)
                {
                    return Result(CameraHealthState.Healthy, true, true, false,
                        port, "RTSP", "Comunicación RTSP y vídeo disponibles.");
                }

                if (rtsp.AuthenticationRequired)
                {
                    return Result(CameraHealthState.AuthenticationRequired, true, false, true,
                        port, "RTSP", "La cámara responde por RTSP pero solicita autenticación para entregar vídeo.");
                }
            }
        }

        foreach (var port in ports.Where(p => p is 80 or 81 or 82 or 88 or 443 or 8080 or 8081 or 8443 or 8888 or 8000))
        {
            var http = await ProbeHttpVideoAsync(device.IpAddress, port, cancellationToken);
            if (http.VideoAvailable)
            {
                return Result(CameraHealthState.Healthy, true, true, false,
                    port, http.Protocol, "Comunicación HTTP/HTTPS y vídeo disponibles.");
            }

            if (http.AuthenticationRequired)
            {
                return Result(CameraHealthState.AuthenticationRequired, true, false, true,
                    port, http.Protocol, "La cámara responde por HTTP/HTTPS pero solicita autenticación para entregar vídeo.");
            }
        }

        var hasKnownCameraEvidence = device.CameraEvidence
            || device.OnvifSupported
            || device.HasOnvifMediaService
            || device.DetectionEvidence.Any(e => e.IsCameraEvidence);

        if (hasKnownCameraEvidence)
        {
            return Result(CameraHealthState.NoVideo, true, false, false,
                ports[0], ProtocolForPort(ports[0]),
                "Hay comunicación con el dispositivo, pero no se pudo confirmar un flujo de vídeo en la comprobación rápida.");
        }

        return Result(CameraHealthState.CommunicationOnly, true, false, false,
            ports[0], ProtocolForPort(ports[0]),
            "El equipo responde en red, pero todavía no hay evidencia suficiente para confirmar vídeo.");
    }

    private static async Task<List<int>> FindOpenPortsAsync(string ip, CancellationToken cancellationToken)
    {
        var tasks = CommonPorts.Select(async port =>
        {
            using var client = new TcpClient();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(ConnectTimeout);
            try
            {
                await client.ConnectAsync(ip, port, timeout.Token);
                return (Port: port, Open: true);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return (port, false); }
            catch (SocketException) { return (port, false); }
        });

        var results = await Task.WhenAll(tasks);
        return results.Where(x => x.Open).Select(x => x.Port).OrderBy(p => p).ToList();
    }

    private static async Task<(bool VideoAvailable, bool AuthenticationRequired, string Protocol)> ProbeRtspAsync(
        string ip,
        int port,
        CancellationToken cancellationToken)
    {
        using var client = new TcpClient();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ReadTimeout);

        try
        {
            await client.ConnectAsync(ip, port, timeout.Token);
            await using var stream = client.GetStream();
            var request = Encoding.ASCII.GetBytes(
                "DESCRIBE rtsp://" + ip + "/ RTSP/1.0\r\n" +
                "CSeq: 1\r\n" +
                "Accept: application/sdp\r\n" +
                "User-Agent: CameraInspector/1.0\r\n\r\n");
            await stream.WriteAsync(request, timeout.Token);
            var buffer = new byte[8192];
            var read = await stream.ReadAsync(buffer, timeout.Token);
            var response = read > 0 ? Encoding.ASCII.GetString(buffer, 0, read) : string.Empty;

            if (response.StartsWith("RTSP/1.0 2", StringComparison.OrdinalIgnoreCase)
                && response.Contains("m=video", StringComparison.OrdinalIgnoreCase))
                return (true, false, "RTSP");

            if (response.StartsWith("RTSP/1.0 401", StringComparison.OrdinalIgnoreCase)
                || response.StartsWith("RTSP/1.0 403", StringComparison.OrdinalIgnoreCase))
                return (false, true, "RTSP");

            return (false, false, "RTSP");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return (false, false, "RTSP");
        }
        catch (SocketException)
        {
            return (false, false, "RTSP");
        }
        catch (IOException)
        {
            return (false, false, "RTSP");
        }
    }

    private static async Task<(bool VideoAvailable, bool AuthenticationRequired, string Protocol)> ProbeHttpVideoAsync(
        string ip,
        int port,
        CancellationToken cancellationToken)
    {
        var https = port is 443 or 8443;
        var protocol = https ? "HTTPS" : "HTTP";
        var scheme = https ? "https" : "http";

        foreach (var path in SnapshotPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, $"{scheme}://{ip}:{port}{path}");
                request.Headers.Accept.Clear();
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("image/jpeg"));
                request.Headers.UserAgent.ParseAdd("CameraInspector/1.0");
                using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

                if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
                    return (false, true, protocol);

                if (!response.IsSuccessStatusCode)
                    continue;

                var contentType = response.Content.Headers.ContentType?.MediaType;
                if (!string.IsNullOrWhiteSpace(contentType)
                    && (contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
                        || contentType.Contains("multipart/x-mixed-replace", StringComparison.OrdinalIgnoreCase)))
                    return (true, false, protocol);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { }
            catch (HttpRequestException) { }
        }

        return (false, false, protocol);
    }

    private static string ProtocolForPort(int port) => port switch
    {
        443 or 8443 => "HTTPS",
        554 or 8554 => "RTSP",
        80 or 81 or 82 or 88 or 8000 or 8080 or 8081 or 8888 => "HTTP",
        _ => "TCP"
    };

    private static CameraHealthResult Result(
        CameraHealthState state,
        bool communication,
        bool video,
        bool auth,
        int? port,
        string? protocol,
        string message) => new()
        {
            State = state,
            CommunicationAvailable = communication,
            VideoAvailable = video,
            AuthenticationRequired = auth,
            CommunicationPort = port,
            Protocol = protocol,
            Message = message,
            CheckedAt = DateTimeOffset.UtcNow
        };
}
