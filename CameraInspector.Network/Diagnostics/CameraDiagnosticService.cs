using System.Diagnostics;
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
        // _onvifDeviceService permite probar ONVIF sin conocer detalles SOAP desde esta clase.
        _onvifDeviceService = onvifDeviceService;

        // _httpClient se entrega por DI y se reutiliza durante toda la vida del servicio.
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
            TestMediaServiceAsync(device, username, password, cancellationToken)
        };

        // WhenAll espera a que todas las pruebas terminen y conserva el resultado individual de cada una.
        var results = await Task.WhenAll(tests);

        // ToList genera una colección independiente para que el consumidor pueda recorrerla sin afectar tareas internas.
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
            // ping es una instancia local porque Ping no se comparte entre operaciones concurrentes de este servicio.
            using var ping = new Ping();

            // reply contiene el resultado ICMP devuelto por Windows.
            var reply = await ping.SendPingAsync(
                ipAddress,
                timeout: 1200,
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
        // port conserva el puerto HTTP descubierto; si aún no conocemos uno usamos el estándar 80.
        var port = device.HttpPort ?? 80;

        // scheme representa el protocolo que utilizaremos para el intento inicial.
        var scheme = device.HttpsSupported && !device.HttpSupported ? "https" : "http";

        // endpoint es la URL utilizada únicamente para comprobar conectividad HTTP.
        var endpoint = $"{scheme}://{device.IpAddress}:{port}/";

        var stopwatch = Stopwatch.StartNew();

        try
        {
            // request se crea por operación para evitar compartir headers o estado entre cámaras.
            using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);

            // response contiene la respuesta HTTP/HTTPS recibida del dispositivo.
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
        // port conserva el puerto detectado; si no existe utilizamos 554, puerto RTSP convencional.
        var port = device.RtspPort ?? 554;
        var stopwatch = Stopwatch.StartNew();

        try
        {
            // client intenta establecer una conexión TCP simple con el endpoint RTSP.
            using var client = new TcpClient();

            await client.ConnectAsync(
                device.IpAddress,
                port,
                cancellationToken);

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

    /// <summary>
    /// Ejecuta GetDeviceInformation y comprueba que exista un Device Service ONVIF funcional.
    /// </summary>
    private async Task<DiagnosticResult> TestOnvifAsync(
        DiscoveredDevice device,
        string? username,
        string? password,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            // info contiene la identidad devuelta por el Device Service cuando la prueba es exitosa.
            var info = await _onvifDeviceService.GetDeviceInformationAsync(
                device,
                username,
                password,
                cancellationToken);

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

    /// <summary>
    /// Comprueba que GetCapabilities anuncie un Media Service utilizable.
    /// </summary>
    private async Task<DiagnosticResult> TestMediaServiceAsync(
        DiscoveredDevice device,
        string? username,
        string? password,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            // capabilities contiene los servicios reales anunciados por la cámara.
            var capabilities = await _onvifDeviceService.GetCapabilitiesAsync(
                device,
                username,
                password,
                cancellationToken);

            stopwatch.Stop();

            if (capabilities is null)
            {
                return new DiagnosticResult
                {
                    TestName = "Media Service",
                    Success = false,
                    Duration = stopwatch.Elapsed,
                    Message = "No se pudieron consultar las capacidades ONVIF."
                };
            }

            if (!capabilities.HasMediaService)
            {
                return new DiagnosticResult
                {
                    TestName = "Media Service",
                    Success = false,
                    NotSupported = true,
                    Duration = stopwatch.Elapsed,
                    Message = "El dispositivo ONVIF no anuncia Media Service."
                };
            }

            return new DiagnosticResult
            {
                TestName = "Media Service",
                Success = true,
                Duration = stopwatch.Elapsed,
                Message = "Media Service disponible."
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
                TestName = "Media Service",
                Success = false,
                Duration = stopwatch.Elapsed,
                Message = ex.Message
            };
        }
    }
}
