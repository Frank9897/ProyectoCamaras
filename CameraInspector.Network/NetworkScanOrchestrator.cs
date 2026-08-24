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

    /// <summary>
    /// Servicio especializado en localizar dispositivos ONVIF mediante WS-Discovery.
    /// </summary>
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
        // candidates contiene todas las direcciones IP que pertenecen a la subred seleccionada.
        var candidates = _subnetCalculator.GetHostAddresses(networkInterface).ToList();

        // total representa el número de direcciones candidatas y se usa para informar progreso a la UI.
        var total = candidates.Count;

        // responsive contiene únicamente las IP que respondieron al ping sweep.
        var responsive = await _pingScanner.ScanAsync(
            candidates,
            cancellationToken: cancellationToken);

        // WS-Discovery no depende de ICMP. Por eso lo ejecutamos igualmente y podemos encontrar
        // cámaras que tengan ICMP bloqueado pero sigan anunciándose mediante ONVIF.
        var onvifResults = await _onvifDiscoveryService.DiscoverAsync(cancellationToken);

        // onvifByIp convierte las respuestas ONVIF en un índice por dirección IP para poder
        // combinar fácilmente WS-Discovery con el resultado del ping sweep.
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

        // Dar un instante al sistema operativo para terminar de poblar la caché ARP después del sweep.
        await Task.Delay(150, cancellationToken);

        // arpTable contiene las relaciones IP -> MAC conocidas por Windows en ese momento.
        var arpTable = _arpResolver.GetArpTable();

        // processedIps impide emitir dos veces el mismo dispositivo cuando una IP aparece
        // simultáneamente en ping y WS-Discovery.
        var processedIps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // scanned representa cuántos dispositivos estamos reportando a la UI en esta etapa.
        var scanned = 0;

        foreach (var ip in responsive)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // ipText es la representación textual reutilizable de la IP actual.
            var ipText = ip.ToString();

            // Se deduplica por IP antes de crear el objeto final.
            if (!processedIps.Add(ipText))
                continue;

            scanned++;

            // mac recibe la dirección física desde ARP si Windows la conoce.
            arpTable.TryGetValue(ip, out var mac);

            // device representa el host encontrado por ping/ARP antes de la identificación posterior.
            var device = new DiscoveredDevice
            {
                IpAddress = ipText,
                MacAddress = mac,
                Status = DeviceStatus.Unknown
            };

            // Si WS-Discovery identificó esta IP, guardamos inmediatamente el Device Service XAddr real.
            if (onvifByIp.TryGetValue(ipText, out var onvifResult))
            {
                device.OnvifSupported = true;
                device.OnvifDeviceServiceXAddr = onvifResult.DeviceServiceXAddr;
                device.OnvifProfile = "detectado por WS-Discovery";
            }

            // update contiene el dispositivo y el progreso actual que se envían a la UI.
            var update = new ScanProgress(scanned, total, device);
            progress?.Report(update);
            yield return update;
        }

        // Las cámaras ONVIF pueden bloquear ICMP. Por eso también agregamos los resultados
        // WS-Discovery que no aparecieron en el ping sweep.
        foreach (var pair in onvifByIp)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // ipText identifica la IP anunciada por el Device Service.
            var ipText = pair.Key;

            if (!processedIps.Add(ipText))
                continue;

            scanned++;

            // TryParse obtiene la IP para consultar ARP. Si no puede convertirse, mac queda nula.
            IPAddress.TryParse(ipText, out var parsedIp);
            var mac = parsedIp is not null && arpTable.TryGetValue(parsedIp, out var knownMac)
                ? knownMac
                : null;

            // device representa una cámara encontrada exclusivamente mediante WS-Discovery.
            var device = new DiscoveredDevice
            {
                IpAddress = ipText,
                MacAddress = mac,
                Status = DeviceStatus.Online,
                OnvifSupported = true,
                OnvifProfile = "detectado por WS-Discovery",
                OnvifDeviceServiceXAddr = pair.Value.DeviceServiceXAddr
            };

            var update = new ScanProgress(scanned, total, device);
            progress?.Report(update);
            yield return update;
        }

        // Si no encontramos nada, emitimos igualmente un evento final para que la UI pueda cerrar
        // el estado de progreso sin depender de que exista al menos un dispositivo.
        if (scanned == 0)
        {
            yield return new ScanProgress(total, total, null);
        }
    }
}
