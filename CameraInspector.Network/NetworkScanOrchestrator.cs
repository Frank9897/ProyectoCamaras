using System.Net;
using CameraInspector.Core.Interfaces;
using CameraInspector.Core.Models;

namespace CameraInspector.Network;

/// <summary>
/// Orquestador principal del descubrimiento de dispositivos.
/// Combina ping/ARP con WS-Discovery, utilizando la interfaz de red seleccionada
/// para que el tráfico de descubrimiento salga por el puerto correcto.
/// </summary>
public sealed class NetworkScanOrchestrator : INetworkScanner
{
    private readonly ISubnetCalculator _subnetCalculator;
    private readonly IPingScanner _pingScanner;
    private readonly IArpResolver _arpResolver;
    private readonly IOnvifDiscoveryService _onvifDiscoveryService;

    public NetworkScanOrchestrator(
        ISubnetCalculator subnetCalculator,
        IPingScanner pingScanner,
        IArpResolver arpResolver,
        IOnvifDiscoveryService onvifDiscoveryService)
    {
        // Cada dependencia representa una parte independiente del pipeline de descubrimiento.
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
        // candidates se calcula exclusivamente desde la IP y máscara del puerto seleccionado.
        var candidates = _subnetCalculator.GetHostAddresses(networkInterface).ToList();

        // total representa la cantidad real de hosts que se explorarán en esa subred.
        var total = candidates.Count;

        // pingTask busca hosts activos dentro de la subred calculada.
        var pingTask = _pingScanner.ScanAsync(candidates, cancellationToken: cancellationToken);

        // onvifTask utiliza la misma interfaz local para emitir y recibir WS-Discovery multicast.
        var onvifTask = _onvifDiscoveryService.DiscoverAsync(networkInterface, cancellationToken);

        // Ambas técnicas avanzan simultáneamente para reducir el tiempo total del descubrimiento.
        await Task.WhenAll(pingTask, onvifTask);

        // responsive contiene las IP que respondieron a ICMP.
        var responsive = await pingTask;
        // onvifResults contiene las cámaras ONVIF que respondieron al Probe multicast.
        var onvifResults = await onvifTask;

        // onvifByIp facilita cruzar la evidencia ONVIF con la obtenida por ping/ARP.
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

        // Damos tiempo a Windows para actualizar la caché ARP con el tráfico generado por el barrido.
        await Task.Delay(150, cancellationToken);
        var arpTable = _arpResolver.GetArpTable();

        // processedIps impide generar duplicados cuando distintas técnicas encuentran la misma cámara.
        var processedIps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var scanned = 0;

        foreach (var ip in responsive)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var ipText = ip.ToString();

            if (!processedIps.Add(ipText))
                continue;

            scanned++;
            arpTable.TryGetValue(ip, out var mac);

            var device = new DiscoveredDevice
            {
                IpAddress = ipText,
                MacAddress = mac,
                Status = DeviceStatus.Unknown
            };

            // Si la misma IP respondió por WS-Discovery, reutilizamos el XAddr ya obtenido.
            if (onvifByIp.TryGetValue(ipText, out var onvifResult))
            {
                device.OnvifSupported = true;
                device.OnvifDeviceServiceXAddr = onvifResult.DeviceServiceXAddr;
                device.OnvifProfile = "detectado por WS-Discovery";
            }

            var update = new ScanProgress(scanned, total, device);
            progress?.Report(update);
            yield return update;
        }

        // Una cámara puede no responder a ICMP. WS-Discovery la conserva como descubrimiento válido.
        foreach (var pair in onvifByIp)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var ipText = pair.Key;

            if (!processedIps.Add(ipText))
                continue;

            scanned++;
            IPAddress.TryParse(ipText, out var parsedIp);

            var mac = parsedIp is not null && arpTable.TryGetValue(parsedIp, out var knownMac)
                ? knownMac
                : null;

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

        // En un escaneo sin resultados, emitimos el progreso final para cerrar correctamente la UI.
        if (scanned == 0)
            yield return new ScanProgress(total, total, null);
    }
}
