using System.Net;
using CameraInspector.Core.Interfaces;
using CameraInspector.Core.Models;

namespace CameraInspector.Network;

/// <summary>
/// Orquestador principal del descubrimiento de dispositivos.
/// Combina ping/ARP con WS-Discovery para detectar tanto equipos que responden a ICMP
/// como cámaras ONVIF que puedan no responder a ping pero sí anunciarse en la red.
/// </summary>
public sealed class NetworkScanOrchestrator : INetworkScanner
{
    /// <summary>Calcula las IP candidatas pertenecientes a la subred seleccionada.</summary>
    private readonly ISubnetCalculator _subnetCalculator;

    /// <summary>Ejecuta el barrido ICMP limitado para detectar hosts accesibles.</summary>
    private readonly IPingScanner _pingScanner;

    /// <summary>Obtiene las MAC conocidas desde la tabla ARP del sistema operativo.</summary>
    private readonly IArpResolver _arpResolver;

    /// <summary>Localiza dispositivos ONVIF mediante WS-Discovery sin depender de ICMP.</summary>
    private readonly IOnvifDiscoveryService _onvifDiscoveryService;

    public NetworkScanOrchestrator(
        ISubnetCalculator subnetCalculator,
        IPingScanner pingScanner,
        IArpResolver arpResolver,
        IOnvifDiscoveryService onvifDiscoveryService)
    {
        _subnetCalculator = subnetCalculator;
        _pingScanner = pingScanner;
        _arpResolver = arpResolver;
        _onvifDiscoveryService = onvifDiscoveryService;
    }

    public async IAsyncEnumerable<ScanProgress> ScanAsync(
        NetworkInterfaceInfo networkInterface,
        IProgress<ScanProgress>? progress = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // candidates contiene todas las direcciones IP pertenecientes a la subred seleccionada.
        var candidates = _subnetCalculator.GetHostAddresses(networkInterface).ToList();

        // total representa cuántas IP candidatas existen y se utiliza para el contador de progreso.
        var total = candidates.Count;

        // pingTask ejecuta el barrido ICMP completo sin esperar al descubrimiento ONVIF.
        var pingTask = _pingScanner.ScanAsync(
            candidates,
            cancellationToken: cancellationToken);

        // onvifTask comienza inmediatamente el Probe multicast WS-Discovery.
        var onvifTask = _onvifDiscoveryService.DiscoverAsync(cancellationToken);

        // Ambas tareas trabajan en paralelo para reducir el tiempo total del escaneo.
        await Task.WhenAll(pingTask, onvifTask);

        // responsive contiene las IP que respondieron al ping sweep.
        var responsive = await pingTask;

        // onvifResults contiene las respuestas ProbeMatch recibidas por WS-Discovery.
        var onvifResults = await onvifTask;

        // onvifByIp convierte cada Device Service XAddr en un índice por IP para poder
        // combinar la evidencia de WS-Discovery con la evidencia de ICMP/ARP.
        var onvifByIp = onvifResults
            .Select(result => new
            {
                Result = result,
                Uri = Uri.TryCreate(result.DeviceServiceXAddr, UriKind.Absolute, out var parsedUri)
                    ? parsedUri
                    : null
            })
            .Where(item => item.Uri is not null && item.Uri.Host.Length > 0)
            .Select(item => new
            {
                item.Result,
                Address = item.Uri!.Host
            })
            .Where(item => IPAddress.TryParse(item.Address, out _))
            .GroupBy(item => item.Address, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Result, StringComparer.OrdinalIgnoreCase);

        // Esperamos brevemente antes de leer ARP para permitir que Windows termine de aprender
        // las direcciones MAC producidas por el tráfico generado durante el barrido.
        await Task.Delay(150, cancellationToken);

        // arpTable contiene las asociaciones IP -> MAC disponibles en Windows en este momento.
        var arpTable = _arpResolver.GetArpTable();

        // processedIps evita emitir un dispositivo dos veces cuando ping y WS-Discovery
        // encuentran exactamente la misma IP.
        var processedIps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // scanned representa cuántos dispositivos únicos ya fueron enviados a la UI.
        var scanned = 0;

        foreach (var ip in responsive)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // ipText es la representación textual de la IP usada por las estructuras de deduplicación.
            var ipText = ip.ToString();

            // Si la IP ya fue procesada por otra fuente, no emitimos una segunda fila.
            if (!processedIps.Add(ipText))
                continue;

            scanned++;

            // mac recibe la dirección física conocida para la IP actual, si existe en ARP.
            arpTable.TryGetValue(ip, out var mac);

            // device representa el host inicial antes de ejecutar la resolución detallada de fabricante.
            var device = new DiscoveredDevice
            {
                IpAddress = ipText,
                MacAddress = mac,
                Status = DeviceStatus.Unknown
            };

            // Si WS-Discovery confirmó la IP, la cámara entra desde el principio con su XAddr real.
            if (onvifByIp.TryGetValue(ipText, out var onvifResult))
            {
                device.OnvifSupported = true;
                device.OnvifDeviceServiceXAddr = onvifResult.DeviceServiceXAddr;
                device.OnvifProfile = "detectado por WS-Discovery";
            }

            // update contiene el dispositivo y el progreso actual que recibe la interfaz.
            var update = new ScanProgress(scanned, total, device);
            progress?.Report(update);
            yield return update;
        }

        // Una cámara ONVIF puede bloquear ICMP. Los resultados WS-Discovery que no aparecieron
        // en el ping sweep se agregan igualmente como dispositivos válidos.
        foreach (var pair in onvifByIp)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // ipText representa la IP obtenida del XAddr anunciado por el dispositivo.
            var ipText = pair.Key;

            // Saltamos dispositivos ya emitidos por el recorrido de ping.
            if (!processedIps.Add(ipText))
                continue;

            scanned++;

            // parsedIp permite consultar ARP para obtener la MAC conocida del dispositivo ONVIF.
            IPAddress.TryParse(ipText, out var parsedIp);

            // mac toma la MAC desde ARP cuando podemos convertir la dirección anunciada a IP.
            var mac = parsedIp is not null && arpTable.TryGetValue(parsedIp, out var knownMac)
                ? knownMac
                : null;

            // device representa un dispositivo descubierto exclusivamente mediante WS-Discovery.
            var device = new DiscoveredDevice
            {
                IpAddress = ipText,
                MacAddress = mac,
                Status = DeviceStatus.Online,
                OnvifSupported = true,
                OnvifProfile = "detectado por WS-Discovery",
                OnvifDeviceServiceXAddr = pair.Value.DeviceServiceXAddr
            };

            // update transporta el descubrimiento hacia la interfaz de usuario.
            var update = new ScanProgress(scanned, total, device);
            progress?.Report(update);
            yield return update;
        }

        // Si no encontramos ningún dispositivo, enviamos un evento final para que la UI cierre
        // correctamente el estado de progreso.
        if (scanned == 0)
        {
            yield return new ScanProgress(total, total, null);
        }
    }
}
