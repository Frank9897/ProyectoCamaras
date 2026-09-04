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
    private static readonly int[] CommonPorts =
    {
        80, 81, 82, 88, 443, 8000, 8080, 8081, 8443, 8554, 8888, 554, 37777, 9000
    };

    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromMilliseconds(450);
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromMilliseconds(700);
    private static readonly HttpClient Http = new(new HttpClientHandler
    {
        AllowAutoRedirect = false,
        // El servicio solo diagnostica disponibilidad. Cámaras suelen usar certificados
        // autofirmados; no se utiliza esta instancia para una operación autenticada.
        ServerCertificateCustomValidationCallback = (_, _, _, _) => true
    })
    {
        Timeout = TimeSpan.FromMilliseconds(800)
    };

    private static readonly string[] SnapshotPaths =
    {
        "/snapshot.jpg", "/snap.jpg", "/image.jpg", "/snapshot.cgi", "/cgi-bin/snapshot.cgi"
    };

    public async Task<CameraHealthResult> CheckAsync(
        DiscoveredDevice device,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(device.IpAddress))
            return Result(CameraHealthState.NoResponse, false, false, false, null, null, "La cámara no tiene una dirección IP válida.");

        var ports = await FindOpenPortsAsync(device.IpAddress, cancellationToken);
        if (ports.Count == 0)
            return Result(CameraHealthState.NoResponse, false, false, false, null, null, "SIN RESPUESTA: no se pudo establecer comunicación TCP con puertos habituales.");

        // Se prueban RTSP y HTTP en paralelo. Una cámara que tarda en responder por un
        // protocolo no debe hacer que el diagnóstico completo espere innecesariamente al otro.
        var rtspPorts = ports.Where(p => p is 554 or 8554).ToArray();
        var httpPorts = ports.Where(p => p is 80 or 81 or 82 or 88 or 443 or 8000 or 8080 or 8081 or 8443 or 8888).ToArray();

        var rtspTasks = rtspPorts.Select(async port =>
        {
            var result = await ProbeRtspAsync(device.IpAddress, port, cancellationToken);
            return (Port: port, Result: result);
        }).ToArray();

        var httpTasks = httpPorts.Select(async port =>
        {
            var result = await ProbeHttpVideoAsync(device.IpAddress, port, cancellationToken);
            return (Port: port, Result: result);
        }).ToArray();

        var allTasks = rtspTasks.Cast<Task<(int Port, (bool VideoAvailable, bool AuthenticationRequired, string Protocol) Result)>>()
            .Concat(httpTasks.Cast<Task<(int Port, (bool VideoAvailable, bool AuthenticationRequired, string Protocol) Result)>>())
            .ToArray();

        var results = await Task.WhenAll(allTasks);

        // Si hay vídeo real confirmado, tiene prioridad sobre una respuesta de autenticación
        // encontrada en otro puerto.
        var video = results
            .Where(item => item.Result.VideoAvailable)
            .OrderBy(item => item.Port)
            .FirstOrDefault();
        if (video.Result.VideoAvailable)
            return Result(CameraHealthState.Healthy, true, true, false, video.Port, video.Result.Protocol, "Comunicación y vídeo disponibles.");

        var auth = results
            .Where(item => item.Result.AuthenticationRequired)
            .OrderBy(item => item.Port)
            .FirstOrDefault();
        if (auth.Result.AuthenticationRequired)
            return Result(CameraHealthState.AuthenticationRequired, true, false, true, auth.Port, auth.Result.Protocol, "El dispositivo responde pero solicita autenticación para entregar vídeo.");

        var hasKnownCameraEvidence = device.CameraEvidence
            || device.OnvifSupported
            || device.HasOnvifMediaService
            || device.DetectionEvidence.Any(e => e.IsCameraEvidence);

        if (hasKnownCameraEvidence)
            return Result(CameraHealthState.NoVideo, true, false, false, ports[0], ProtocolForPort(ports[0]), "ALERTA: hay comunicación con el dispositivo, pero no se pudo confirmar vídeo en la comprobación rápida.");

        return Result(CameraHealthState.CommunicationOnly, true, false, false, ports[0], ProtocolForPort(ports[0]), "ALERTA: el equipo responde en red, pero no hay evidencia suficiente para confirmar vídeo.");
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
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return (Port: port, Open: false); }
            catch (SocketException) { return (Port: port, Open: false); }
        });

        var results = await Task.WhenAll(tasks);
        return results.Where(x => x.Open).Select(x => x.Port).OrderBy(p => p).ToList();
    }

    private static async Task<(bool VideoAvailable, bool AuthenticationRequired, string Protocol)> ProbeRtspAsync(string ip, int port, CancellationToken cancellationToken)
    {
        using var client = new TcpClient();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ReadTimeout);

        try
        {
            await client.ConnectAsync(ip, port, timeout.Token);
            await using var stream = client.GetStream();
            var request = Encoding.ASCII.GetBytes(
                $"DESCRIBE rtsp://{ip}:{port}/ RTSP/1.0\r\n" +
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
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return (false, false, "RTSP"); }
        catch (SocketException) { return (false, false, "RTSP"); }
        catch (IOException) { return (false, false, "RTSP"); }
    }

    private static async Task<(bool VideoAvailable, bool AuthenticationRequired, string Protocol)> ProbeHttpVideoAsync(string ip, int port, CancellationToken cancellationToken)
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

    private static CameraHealthResult Result(CameraHealthState state, bool communication, bool video, bool auth, int? port, string? protocol, string message) => new()
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
