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
///
/// ETAPA 1 del plan de diagnóstico (recomendaciones): cada resultado ahora incluye
/// Severity (qué tan urgente es), Category (a qué área pertenece) y RecommendedAction
/// (qué hacer, en lenguaje de service, no el mensaje técnico crudo). Las pruebas ONVIF
/// además atenúan su severidad cuando el fabricante detectado es una legacy conocida
/// (VIVOTEK/DAHUA/HIKVISION) que no expone ONVIF real: para esos casos, no responder a
/// ONVIF es el comportamiento ESPERADO, no una falla, así que queda en Info en vez de
/// Advertencia.
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
    /// Detecta, por manufacturer/model, si el dispositivo es de un fabricante legacy
    /// conocido (VIVOTEK/DAHUA/HIKVISION) que típicamente NO expone ONVIF real. Mismo
    /// criterio que NetworkConfigurationEditViewModel.DetectLegacyWriter, duplicado acá
    /// porque este proyecto (CameraInspector.Network) no depende de la capa de App.
    /// </summary>
    private static bool IsKnownLegacyVendor(DiscoveredDevice device)
    {
        var manufacturer = device.Manufacturer ?? string.Empty;
        var model = device.Model ?? string.Empty;

        return manufacturer.Contains("VIVOTEK", StringComparison.OrdinalIgnoreCase)
               || model.Contains("IP71", StringComparison.OrdinalIgnoreCase)
               || manufacturer.Contains("Dahua", StringComparison.OrdinalIgnoreCase)
               || manufacturer.Contains("Amcrest", StringComparison.OrdinalIgnoreCase)
               || manufacturer.Contains("Hikvision", StringComparison.OrdinalIgnoreCase)
               || model.StartsWith("DS-", StringComparison.OrdinalIgnoreCase);
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
                    Severity = DiagnosticSeverity.Critico,
                    Category = DiagnosticCategory.Red,
                    Message = $"La dirección IP '{ipAddress}' no es válida.",
                    RecommendedAction = "Vuelva a escanear la red: la IP registrada para este dispositivo no tiene un formato válido."
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
            var success = reply.Status == IPStatus.Success;

            return new DiagnosticResult
            {
                TestName = "Ping",
                Success = success,
                Duration = stopwatch.Elapsed,
                Severity = success ? DiagnosticSeverity.Info : DiagnosticSeverity.Critico,
                Category = DiagnosticCategory.Red,
                Message = success
                    ? $"Respuesta ICMP: {reply.RoundtripTime} ms"
                    : $"Estado ICMP: {reply.Status}",
                RecommendedAction = success
                    ? null
                    : "Sin respuesta ICMP: verifique cableado/switch, que la cámara esté encendida, y que la IP no haya cambiado (vuelva a escanear la red antes de asumir que el equipo está caído)."
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
                Severity = DiagnosticSeverity.Critico,
                Category = DiagnosticCategory.Red,
                Message = ex.Message,
                RecommendedAction = "Error inesperado al hacer ping. Verifique que el adaptador de red del PC esté activo."
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
                Severity = authenticationRequired ? DiagnosticSeverity.Advertencia : DiagnosticSeverity.Info,
                Category = authenticationRequired ? DiagnosticCategory.Autenticacion : DiagnosticCategory.Red,
                Message = authenticationRequired
                    ? $"HTTP respondió {(int)response.StatusCode} ({response.StatusCode}) · autenticación requerida{serverText}"
                    : $"HTTP respondió {(int)response.StatusCode} ({response.StatusCode}){serverText}",
                RecommendedAction = authenticationRequired
                    ? "El servicio web pide autenticación: verifique que las credenciales guardadas sigan vigentes en la cámara (pudo haber sido reconfigurada por otra persona)."
                    : null
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
                Severity = DiagnosticSeverity.Advertencia,
                Category = DiagnosticCategory.Red,
                Message = ex.Message,
                RecommendedAction = "No se pudo conectar al servicio web. Puede estar deshabilitado, en otro puerto, o el servicio HTTP de la cámara caído; si Ping funcionó, pruebe igual RTSP para video."
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
                    Severity = DiagnosticSeverity.Critico,
                    Category = DiagnosticCategory.Video,
                    Message = $"La dirección IP '{device.IpAddress}' no es válida.",
                    RecommendedAction = "Vuelva a escanear la red: la IP registrada para este dispositivo no tiene un formato válido."
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
                Severity = client.Connected ? DiagnosticSeverity.Info : DiagnosticSeverity.Critico,
                Category = DiagnosticCategory.Video,
                Message = client.Connected
                    ? $"Puerto TCP {port} accesible"
                    : $"Puerto TCP {port} no conectado",
                RecommendedAction = client.Connected
                    ? null
                    : $"El puerto RTSP {port} no está accesible: sin este puerto no hay video posible. Verifique que el streaming esté habilitado en la cámara o que un firewall/router no lo esté bloqueando."
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
                Severity = DiagnosticSeverity.Critico,
                Category = DiagnosticCategory.Video,
                Message = $"Puerto {port}: {ex.Message}",
                RecommendedAction = $"El puerto RTSP {port} no está accesible: sin este puerto no hay video posible. Verifique que el streaming esté habilitado en la cámara o que un firewall/router no lo esté bloqueando."
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
                    Severity = DiagnosticSeverity.Critico,
                    Category = DiagnosticCategory.Video,
                    Message = $"La dirección IP '{device.IpAddress}' no es válida.",
                    RecommendedAction = "Vuelva a escanear la red: la IP registrada para este dispositivo no tiene un formato válido."
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
                    Severity = DiagnosticSeverity.Advertencia,
                    Category = DiagnosticCategory.Video,
                    Message = "El puerto aceptó TCP pero no devolvió respuesta RTSP.",
                    RecommendedAction = "El puerto abre pero no habla RTSP: verifique que el puerto configurado en la cámara sea realmente el de streaming (algunos equipos usan un puerto RTSP no estándar)."
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
                    Severity = DiagnosticSeverity.Advertencia,
                    Category = DiagnosticCategory.Video,
                    Message = "El servicio respondió, pero no se reconoció una respuesta RTSP válida.",
                    RecommendedAction = "El puerto responde pero no con el protocolo RTSP esperado; puede ser otro servicio escuchando en ese puerto."
                };
            }

            var parts = statusLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var statusCode = parts.Length >= 2 && int.TryParse(parts[1], out var parsedCode) ? parsedCode : 0;
            var authenticationRequired = statusCode is 401 or 403;
            var success = statusCode is >= 200 and < 500;

            return new DiagnosticResult
            {
                TestName = "RTSP protocolo",
                Success = success,
                Duration = stopwatch.Elapsed,
                Severity = authenticationRequired ? DiagnosticSeverity.Advertencia : (success ? DiagnosticSeverity.Info : DiagnosticSeverity.Advertencia),
                Category = authenticationRequired ? DiagnosticCategory.Autenticacion : DiagnosticCategory.Video,
                Message = authenticationRequired
                    ? $"RTSP respondió {statusCode}: autenticación requerida"
                    : $"Respuesta RTSP válida: {statusCode}",
                RecommendedAction = authenticationRequired
                    ? "El servicio RTSP exige autenticación: revise las credenciales guardadas, o pruebe el acceso de fábrica si la cámara pudo haber sido reconfigurada."
                    : (success ? null : "Respuesta RTSP inesperada; revise la ruta de streaming configurada en la cámara.")
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
                Severity = DiagnosticSeverity.Advertencia,
                Category = DiagnosticCategory.Video,
                Message = $"RTSP {device.IpAddress}:{port}: {ex.Message}",
                RecommendedAction = "No se pudo completar el diálogo RTSP; si el puerto TCP sí respondió, revise si la cámara limita conexiones simultáneas o exige un User-Agent específico."
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
                var isKnownLegacy = IsKnownLegacyVendor(device);
                return new DiagnosticResult
                {
                    TestName = "ONVIF Device",
                    Success = false,
                    Duration = stopwatch.Elapsed,
                    // Que un VIVOTEK/DAHUA/HIKVISION legacy no responda ONVIF es lo ESPERADO,
                    // no una falla: se atenúa a Info para no alarmar al técnico sin motivo.
                    Severity = isKnownLegacy ? DiagnosticSeverity.Info : DiagnosticSeverity.Advertencia,
                    Category = DiagnosticCategory.Configuracion,
                    Message = "Device Service no respondió correctamente o requiere autenticación.",
                    RecommendedAction = isKnownLegacy
                        ? "Esperado para este fabricante: no expone ONVIF real. Use la pestaña de Configuración de Red (CGI/ISAPI específico) en vez de ONVIF."
                        : "Verifique que ONVIF esté habilitado en la cámara y que las credenciales guardadas sean correctas."
                };
            }

            return new DiagnosticResult
            {
                TestName = "ONVIF Device",
                Success = true,
                Duration = stopwatch.Elapsed,
                Severity = DiagnosticSeverity.Info,
                Category = DiagnosticCategory.Configuracion,
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
            var isKnownLegacy = IsKnownLegacyVendor(device);
            return new DiagnosticResult
            {
                TestName = "ONVIF Device",
                Success = false,
                Duration = stopwatch.Elapsed,
                Severity = isKnownLegacy ? DiagnosticSeverity.Info : DiagnosticSeverity.Advertencia,
                Category = DiagnosticCategory.Configuracion,
                Message = ex.Message,
                RecommendedAction = isKnownLegacy
                    ? "Esperado para este fabricante: no expone ONVIF real. Use la pestaña de Configuración de Red (CGI/ISAPI específico) en vez de ONVIF."
                    : "Verifique que ONVIF esté habilitado en la cámara y que las credenciales guardadas sean correctas."
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
                var isKnownLegacy = IsKnownLegacyVendor(device);
                return new DiagnosticResult
                {
                    TestName = "ONVIF capacidades",
                    Success = false,
                    Duration = stopwatch.Elapsed,
                    Severity = isKnownLegacy ? DiagnosticSeverity.Info : DiagnosticSeverity.Advertencia,
                    Category = DiagnosticCategory.Configuracion,
                    Message = "No se pudieron consultar las capacidades ONVIF. El servicio puede requerir autenticación.",
                    RecommendedAction = isKnownLegacy
                        ? "Esperado para este fabricante: no expone ONVIF real."
                        : "Verifique credenciales y que ONVIF esté habilitado en la cámara."
                };
            }

            return new DiagnosticResult
            {
                TestName = "ONVIF capacidades",
                Success = capabilities.HasMediaService,
                NotSupported = !capabilities.HasMediaService,
                Duration = stopwatch.Elapsed,
                Severity = capabilities.HasMediaService ? DiagnosticSeverity.Info : DiagnosticSeverity.Info,
                Category = DiagnosticCategory.Configuracion,
                Message = BuildCapabilitiesMessage(capabilities),
                RecommendedAction = capabilities.HasMediaService
                    ? null
                    : "La cámara responde ONVIF pero no anuncia servicio de Media: el video probablemente se administra por RTSP directo del fabricante, no por perfiles ONVIF."
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            var isKnownLegacy = IsKnownLegacyVendor(device);
            return new DiagnosticResult
            {
                TestName = "ONVIF capacidades",
                Success = false,
                Duration = stopwatch.Elapsed,
                Severity = isKnownLegacy ? DiagnosticSeverity.Info : DiagnosticSeverity.Advertencia,
                Category = DiagnosticCategory.Configuracion,
                Message = ex.Message,
                RecommendedAction = isKnownLegacy
                    ? "Esperado para este fabricante: no expone ONVIF real."
                    : "Verifique credenciales y que ONVIF esté habilitado en la cámara."
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
                    Severity = DiagnosticSeverity.Info,
                    Category = DiagnosticCategory.Configuracion,
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
                var isKnownLegacy = IsKnownLegacyVendor(device);
                return new DiagnosticResult
                {
                    TestName = "ONVIF red",
                    Success = false,
                    Duration = stopwatch.Elapsed,
                    Severity = isKnownLegacy ? DiagnosticSeverity.Info : DiagnosticSeverity.Advertencia,
                    Category = DiagnosticCategory.Configuracion,
                    Message = "La cámara no devolvió una configuración de red ONVIF válida.",
                    RecommendedAction = isKnownLegacy
                        ? "Esperado para este fabricante: use la pestaña de Configuración de Red, que ya sabe hablar su CGI/ISAPI propio en vez de ONVIF."
                        : "Las credenciales pueden haber sido rechazadas, o la cámara no expone configuración de red por ONVIF pese a responder a otras consultas."
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
                Severity = DiagnosticSeverity.Info,
                Category = DiagnosticCategory.Configuracion,
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
            var isKnownLegacy = IsKnownLegacyVendor(device);
            return new DiagnosticResult
            {
                TestName = "ONVIF red",
                Success = false,
                Duration = stopwatch.Elapsed,
                Severity = isKnownLegacy ? DiagnosticSeverity.Info : DiagnosticSeverity.Advertencia,
                Category = DiagnosticCategory.Configuracion,
                Message = ex.Message,
                RecommendedAction = isKnownLegacy
                    ? "Esperado para este fabricante: use la pestaña de Configuración de Red en vez de ONVIF."
                    : "Revise credenciales y disponibilidad del servicio ONVIF de la cámara."
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
