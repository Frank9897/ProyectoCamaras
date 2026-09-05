using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using CameraInspector.Core.Interfaces;
using CameraInspector.Core.Models;

namespace CameraInspector.Network.Diagnostics;

/// <summary>
/// Implementación de la batería de diagnóstico profesional.
/// Las pruebas independientes se ejecutan en paralelo para reducir el tiempo total.
/// </summary>
public sealed class CameraDiagnosticService : ICameraDiagnosticService
{
    private readonly IOnvifDeviceService _onvifDeviceService;

    // _httpClient reutiliza conexiones HTTP y evita crear un HttpClient nuevo por cada prueba.
    private readonly HttpClient _httpClient;

    public CameraDiagnosticService(
        IOnvifDeviceService onvifDeviceService,
        HttpClient httpClient)
    {
        _onvifDeviceService = onvifDeviceService;
        _httpClient = httpClient;
    }

    public async Task<IReadOnlyList<DiagnosticResult>> RunAsync(
        DiscoveredDevice device,
        string? username,
        string? password,
        CancellationToken cancellationToken = default)
    {
        // Cada tarea representa una prueba independiente. Ejecutarlas juntas reduce la duración total.
        var tests = new Task<DiagnosticResult>[]
        {
            TestPingAsync(device.IpAddress, cancellationToken),
            TestHttpAsync(device, cancellationToken),
            TestRtspPortAsync(device, cancellationToken),
            TestRtspProtocolAsync(device, cancellationToken),
            TestOnvifAsync(device, username, password, cancellationToken),
            TestOnvifCapabilitiesAsync(device, username, password, cancellationToken),
            TestOnvifNetworkAsync(device, username, password, cancellationToken)
        };

        var results = await Task.WhenAll(tests);
        return results.ToList();
    }

    /// <summary>
    /// Comprueba conectividad IP mediante ICMP.
    /// </summary>
    private static async Task<DiagnosticResult> TestPingAsync(
        string ipAddress,
        CancellationToken cancellationToken)
    {
        // stopwatch mide únicamente el tiempo consumido por la operación de ping.
        var stopwatch = Stopwatch.StartNew();

        try
        {
            // parsedAddress convierte la IP textual al tipo requerido por el overload de Ping que admite cancelación.
            if (!IPAddress.TryParse(ipAddress, out var parsedAddress))
            {
                stopwatch.Stop();
                return new DiagnosticResult
                {
                    TestName = "Ping",
                    Success = false,
                    Duration = stopwatch.Elapsed,
                    Message = $"La dirección IP '{ipAddress}' no es válida."
                };
            }

            // ping es una instancia local porque no se comparte entre diagnósticos concurrentes.
            using var ping = new Ping();

            // El timeout se expresa como TimeSpan porque es la firma disponible en .NET 9 para este overload.
            var timeout = TimeSpan.FromMilliseconds(1200);

            // payload representa los datos ICMP enviados. Un bloque pequeño mantiene la prueba liviana.
            var payload = new byte[32];

            // options evita solicitar fragmentación del paquete ICMP.
            var options = new PingOptions { DontFragment = false };

            // reply contiene el resultado ICMP devuelto por Windows.
            var reply = await ping.SendPingAsync(
                parsedAddress,
                timeout,
                payload,
                options,
                cancellationToken);

            stopwatch.Stop();

            return new DiagnosticResult
            {
                TestName = "Ping",
                Success = reply.Status == IPStatus.Success,
                Duration = stopwatch.Elapsed,
                Message = reply.Status == IPStatus.Success
                    ? $"Respuesta ICMP: {reply.RoundtripTime} ms"
                    : $"Estado ICMP: {reply.Status}"
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return new DiagnosticResult
            {
                TestName = "Ping",
                Success = false,
                Duration = stopwatch.Elapsed,
                Message = ex.Message
            };
        }
    }

    /// <summary>
    /// Comprueba si el servicio HTTP responde en el puerto conocido o por defecto 80.
    /// </summary>
    private async Task<DiagnosticResult> TestHttpAsync(
        DiscoveredDevice device,
        CancellationToken cancellationToken)
    {
        var port = device.HttpPort ?? 80;
        var scheme = device.HttpsSupported && !device.HttpSupported ? "https" : "http";
        var endpoint = $"{scheme}://{device.IpAddress}:{port}/";
        var stopwatch = Stopwatch.StartNew();

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            stopwatch.Stop();
            var authenticationRequired = response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden;
            var server = response.Headers.Server.ToString();
            var serverText = string.IsNullOrWhiteSpace(server) ? string.Empty : $" · Server: {server}";

            return new DiagnosticResult
            {
                TestName = "HTTP",
                Success = true,
                Duration = stopwatch.Elapsed,
                Message = authenticationRequired
                    ? $"HTTP respondió {(int)response.StatusCode} ({response.StatusCode}) · autenticación requerida{serverText}"
                    : $"HTTP respondió {(int)response.StatusCode} ({response.StatusCode}){serverText}"
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return new DiagnosticResult
            {
                TestName = "HTTP",
                Success = false,
                Duration = stopwatch.Elapsed,
                Message = ex.Message
            };
        }
    }

    /// <summary>
    /// Comprueba que el puerto RTSP esté accesible por TCP.
    /// Esto no autentica ni reproduce todavía; únicamente valida el transporte.
    /// </summary>
    private static async Task<DiagnosticResult> TestRtspPortAsync(
        DiscoveredDevice device,
        CancellationToken cancellationToken)
    {
        var port = device.RtspPort ?? 554;
        var stopwatch = Stopwatch.StartNew();

        try
        {
            if (!IPAddress.TryParse(device.IpAddress, out var parsedAddress))
            {
                stopwatch.Stop();
                return new DiagnosticResult
                {
                    TestName = "RTSP TCP",
                    Success = false,
                    Duration = stopwatch.Elapsed,
                    Message = $"La dirección IP '{device.IpAddress}' no es válida."
                };
            }

            using var client = new TcpClient();
            await client.ConnectAsync(parsedAddress, port, cancellationToken);

            stopwatch.Stop();
            return new DiagnosticResult
            {
                TestName = "RTSP TCP",
                Success = client.Connected,
                Duration = stopwatch.Elapsed,
                Message = client.Connected
                    ? $"Puerto TCP {port} accesible"
                    : $"Puerto TCP {port} no conectado"
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return new DiagnosticResult
            {
                TestName = "RTSP TCP",
                Success = false,
                Duration = stopwatch.Elapsed,
                Message = $"Puerto {port}: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Realiza un OPTIONS RTSP real para diferenciar un puerto abierto de un servidor RTSP que entiende el protocolo.
    /// </summary>
    private static async Task<DiagnosticResult> TestRtspProtocolAsync(
        DiscoveredDevice device,
        CancellationToken cancellationToken)
    {
        var port = device.RtspPort ?? 554;
        var stopwatch = Stopwatch.StartNew();

        try
        {
            if (!IPAddress.TryParse(device.IpAddress, out var parsedAddress))
            {
                stopwatch.Stop();
                return new DiagnosticResult
                {
                    TestName = "RTSP protocolo",
                    Success = false,
                    Duration = stopwatch.Elapsed,
                    Message = $"La dirección IP '{device.IpAddress}' no es válida."
                };
            }

            using var client = new TcpClient();
            await client.ConnectAsync(parsedAddress, port, cancellationToken);
            using var stream = client.GetStream();
            stream.ReadTimeout = 1500;
            stream.WriteTimeout = 1500;

            var request = Encoding.ASCII.GetBytes(
                $"OPTIONS rtsp://{device.IpAddress}:{port}/ RTSP/1.0\r\nCSeq: 1\r\nUser-Agent: CameraInspector\r\n\r\n");
            await stream.WriteAsync(request, cancellationToken);
            await stream.FlushAsync(cancellationToken);

            var buffer = new byte[4096];
            var bytesRead = await stream.ReadAsync(buffer, cancellationToken);
            stopwatch.Stop();

            if (bytesRead <= 0)
            {
                return new DiagnosticResult
                {
                    TestName = "RTSP protocolo",
                    Success = false,
                    Duration = stopwatch.Elapsed,
                    Message = "El puerto aceptó TCP pero no devolvió respuesta RTSP."
                };
            }

            var response = Encoding.ASCII.GetString(buffer, 0, bytesRead);
            var statusLine = response
                .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault(line => line.StartsWith("RTSP/", StringComparison.OrdinalIgnoreCase));

            if (string.IsNullOrWhiteSpace(statusLine))
            {
                return new DiagnosticResult
                {
                    TestName = "RTSP protocolo",
                    Success = false,
                    Duration = stopwatch.Elapsed,
                    Message = "El servicio respondió, pero no se reconoció una respuesta RTSP válida."
                };
            }

            var parts = statusLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var statusCode = parts.Length >= 2 && int.TryParse(parts[1], out var parsedCode) ? parsedCode : 0;
            var authenticationRequired = statusCode is 401 or 403;

            return new DiagnosticResult
            {
                TestName = "RTSP protocolo",
                Success = statusCode is >= 200 and < 500,
                Duration = stopwatch.Elapsed,
                Message = authenticationRequired
                    ? $"RTSP respondió {statusCode}: autenticación requerida"
                    : $"Respuesta RTSP válida: {statusCode}"
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return new DiagnosticResult
            {
                TestName = "RTSP protocolo",
                Success = false,
                Duration = stopwatch.Elapsed,
                Message = $"RTSP {device.IpAddress}:{port}: {ex.Message}"
            };
        }
    }

    private async Task<DiagnosticResult> TestOnvifAsync(
        DiscoveredDevice device,
        string? username,
        string? password,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var info = await _onvifDeviceService.GetDeviceInformationAsync(device, username, password, cancellationToken);
            stopwatch.Stop();
            if (info is null)
            {
                return new DiagnosticResult
                {
                    TestName = "ONVIF Device",
                    Success = false,
                    Duration = stopwatch.Elapsed,
                    Message = "Device Service no respondió correctamente o requiere autenticación."
                };
            }

            return new DiagnosticResult
            {
                TestName = "ONVIF Device",
                Success = true,
                Duration = stopwatch.Elapsed,
                Message = $"ONVIF OK: {info.Manufacturer ?? "Fabricante desconocido"} {info.Model ?? "Modelo desconocido"} · Firmware: {info.FirmwareVersion ?? "sin dato"} · Serial: {info.SerialNumber ?? "sin dato"}"
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return new DiagnosticResult
            {
                TestName = "ONVIF Device",
                Success = false,
                Duration = stopwatch.Elapsed,
                Message = ex.Message
            };
        }
    }

    private async Task<DiagnosticResult> TestOnvifCapabilitiesAsync(
        DiscoveredDevice device,
        string? username,
        string? password,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var capabilities = await _onvifDeviceService.GetCapabilitiesAsync(device, username, password, cancellationToken);
            stopwatch.Stop();
            if (capabilities is null)
            {
                return new DiagnosticResult
                {
                    TestName = "ONVIF capacidades",
                    Success = false,
                    Duration = stopwatch.Elapsed,
                    Message = "No se pudieron consultar las capacidades ONVIF. El servicio puede requerir autenticación."
                };
            }

            return new DiagnosticResult
            {
                TestName = "ONVIF capacidades",
                Success = capabilities.HasMediaService,
                NotSupported = !capabilities.HasMediaService,
                Duration = stopwatch.Elapsed,
                Message = BuildCapabilitiesMessage(capabilities)
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return new DiagnosticResult
            {
                TestName = "ONVIF capacidades",
                Success = false,
                Duration = stopwatch.Elapsed,
                Message = ex.Message
            };
        }
    }

    /// <summary>
    /// Consulta la configuración de red ONVIF en modo lectura para detectar interfaces y gateways anunciados.
    /// </summary>
    private async Task<DiagnosticResult> TestOnvifNetworkAsync(
        DiscoveredDevice device,
        string? username,
        string? password,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                return new DiagnosticResult
                {
                    TestName = "ONVIF red",
                    Success = false,
                    NotSupported = true,
                    Duration = stopwatch.Elapsed,
                    Message = "Se omitió la consulta de red ONVIF porque no hay credenciales disponibles."
                };
            }

            var configuration = await _onvifDeviceService.GetNetworkConfigurationAsync(
                device,
                username,
                password,
                cancellationToken);
            stopwatch.Stop();

            if (configuration is null)
            {
                return new DiagnosticResult
                {
                    TestName = "ONVIF red",
                    Success = false,
                    Duration = stopwatch.Elapsed,
                    Message = "La cámara no devolvió una configuración de red ONVIF válida."
                };
            }

            var interfaces = configuration.Interfaces.Count;
            var gateways = configuration.IPv4Gateways.Count;
            var protocols = configuration.Protocols.Count;
            return new DiagnosticResult
            {
                TestName = "ONVIF red",
                Success = true,
                Duration = stopwatch.Elapsed,
                Message = $"Configuración ONVIF de red disponible · Interfaces: {interfaces} · Gateways IPv4: {gateways} · Protocolos: {protocols}"
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return new DiagnosticResult
            {
                TestName = "ONVIF red",
                Success = false,
                Duration = stopwatch.Elapsed,
                Message = ex.Message
            };
        }
    }

    private static string BuildCapabilitiesMessage(OnvifServiceCapabilities capabilities)
    {
        static string State(bool available) => available ? "disponible" : "no anunciado";

        return $"Device: {(!string.IsNullOrWhiteSpace(capabilities.DeviceServiceXAddr) ? "disponible" : "no anunciado")} · " +
               $"Media: {State(capabilities.HasMediaService)} · " +
               $"Imaging: {State(capabilities.HasImagingService)} · " +
               $"PTZ: {State(capabilities.HasPtzService)} · " +
               $"Events: {State(capabilities.HasEventsService)}";
    }
}
