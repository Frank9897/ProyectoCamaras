using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using CameraInspector.Core.Interfaces;
using CameraInspector.Core.Models;

namespace CameraInspector.Network.Diagnostics;

/// <summary>
/// Implementación de la batería de diagnóstico rápido.
/// Las pruebas son independientes y se ejecutan en paralelo para reducir el tiempo total.
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
            TestOnvifAsync(device, username, password, cancellationToken),
            TestOnvifCapabilitiesAsync(device, username, password, cancellationToken)
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
            return new DiagnosticResult
            {
                TestName = "HTTP",
                Success = true,
                Duration = stopwatch.Elapsed,
                Message = $"HTTP respondió {(int)response.StatusCode} ({response.StatusCode})"
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
                    TestName = "RTSP",
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
                TestName = "RTSP",
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
                TestName = "RTSP",
                Success = false,
                Duration = stopwatch.Elapsed,
                Message = $"Puerto {port}: {ex.Message}"
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
                    TestName = "ONVIF",
                    Success = false,
                    Duration = stopwatch.Elapsed,
                    Message = "Device Service no respondió correctamente o requiere autenticación."
                };
            }

            return new DiagnosticResult
            {
                TestName = "ONVIF",
                Success = true,
                Duration = stopwatch.Elapsed,
                Message = $"ONVIF OK: {info.Manufacturer ?? "Fabricante desconocido"} {info.Model ?? "Modelo desconocido"}"
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
                TestName = "ONVIF",
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
