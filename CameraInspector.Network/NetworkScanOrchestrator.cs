using System.Net;
using CameraInspector.Core.Interfaces;
using CameraInspector.Core.Models;

namespace CameraInspector.Network;

/// <summary>
/// Orquestador principal del descubrimiento de dispositivos.
/// Combina ping/ARP con WS-Discovery y discovery propietario VIVOTEK,
/// y permite limitar el alcance para cámaras conectadas directamente o redes completas.
/// </summary>
public sealed class NetworkScanOrchestrator : INetworkScanner
{
    private readonly ISubnetCalculator _subnetCalculator;
    private readonly IPingScanner _pingScanner;
    private readonly IArpResolver _arpResolver;
    private readonly IOnvifDiscoveryService _onvifDiscoveryService;
    private readonly IVivotekDiscoveryService _vivotekDiscoveryService;

    public NetworkScanOrchestrator(
        ISubnetCalculator subnetCalculator,
        IPingScanner pingScanner,
        IArpResolver arpResolver,
        IOnvifDiscoveryService onvifDiscoveryService,
        IVivotekDiscoveryService vivotekDiscoveryService)
    {
        // Cada dependencia representa una técnica independiente del pipeline de descubrimiento.
        _subnetCalculator = subnetCalculator;
        _pingScanner = pingScanner;
        _arpResolver = arpResolver;
        _onvifDiscoveryService = onvifDiscoveryService;
        _vivotekDiscoveryService = vivotekDiscoveryService;
    }

    public async IAsyncEnumerable<ScanProgress> ScanAsync(
        NetworkInterfaceInfo networkInterface,
        IProgress<ScanProgress>? progress = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default,
        DiscoveryScanMode mode = DiscoveryScanMode.NetworkSubnet)
    {
        // candidates contiene las IP de la subred solamente cuando el modo necesita barrido de hosts.
        // En DirectCamera queda vacío para evitar que una cámara conectada directamente provoque un sweep masivo.
        var candidates = mode == DiscoveryScanMode.DirectCamera
            ? new List<IPAddress>()
            : _subnetCalculator.GetHostAddresses(networkInterface).ToList();

        // pingTask queda vacío en modo directo; en los otros modos recorre la subred calculada.
        var pingTask = mode == DiscoveryScanMode.DirectCamera
            ? Task.FromResult<IReadOnlyList<IPAddress>>(Array.Empty<IPAddress>())
            : _pingScanner.ScanAsync(candidates, cancellationToken: cancellationToken);

        // onvifTask realiza WS-Discovery siempre sobre la interfaz elegida.
        var onvifTask = _onvifDiscoveryService.DiscoverAsync(networkInterface, cancellationToken);

        // vivotekTask realiza el broadcast propietario de VIVOTEK tanto en modo directo como de red.
        var vivotekTask = _vivotekDiscoveryService.DiscoverAsync(networkInterface, cancellationToken);

        // Ejecutamos los métodos de descubrimiento en paralelo para reducir el tiempo total.
        await Task.WhenAll(pingTask, onvifTask, vivotekTask);

        // responsive contiene las IP que respondieron a ICMP cuando el modo permite ping sweep.
        var responsive = await pingTask;
        // onvifResults contiene cámaras ONVIF descubiertas por multicast.
        var onvifResults = await onvifTask;
        // vivotekResults contiene dispositivos VIVOTEK descubiertos por su protocolo propietario.
        var vivotekResults = await vivotekTask;

        // onvifByIp permite cruzar la evidencia ONVIF con ping y discovery propietario.
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

        // Damos tiempo a Windows para actualizar la caché ARP después del tráfico de discovery.
        await Task.Delay(150, cancellationToken);
        var arpTable = _arpResolver.GetArpTable();

        // totalFound mide la evidencia real cuando no existe un sweep de hosts.
        var discoveredCount = responsive.Count + onvifByIp.Count + vivotekResults.Count;
        var total = Math.Max(candidates.Count, discoveredCount);

        // processedIps evita generar duplicados cuando dos o más protocolos encuentran el mismo equipo.
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

            // Si la misma IP respondió por WS-Discovery, agregamos esa evidencia al mismo dispositivo.
            if (onvifByIp.TryGetValue(ipText, out var onvifResult))
            {
                device.OnvifSupported = true;
                device.OnvifDeviceServiceXAddr = onvifResult.DeviceServiceXAddr;
                device.OnvifProfile = "detectado por WS-Discovery";
            }

            // Si VIVOTEK también anunció la IP, asociamos el fabricante sin autenticarnos.
            var vivotekMatch = vivotekResults.FirstOrDefault(item =>
                string.Equals(item.IpAddress, ipText, StringComparison.OrdinalIgnoreCase));
            if (vivotekMatch is not null)
            {
                device.Manufacturer = "VIVOTEK";
                device.AssignedProviderName = "VIVOTEK";
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

            // Si VIVOTEK también anunció la misma IP, agregamos la marca del fabricante.
            if (vivotekResults.Any(item => string.Equals(item.IpAddress, ipText, StringComparison.OrdinalIgnoreCase)))
            {
                device.Manufacturer = "VIVOTEK";
                device.AssignedProviderName = "VIVOTEK";
            }

            var update = new ScanProgress(scanned, total, device);
            progress?.Report(update);
            yield return update;
        }

        // VIVOTEK puede ser la única evidencia de una cámara conectada directamente, incluso sin ping ni ONVIF.
        foreach (var vivotekDevice in vivotekResults)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var ipText = vivotekDevice.IpAddress;

            if (!processedIps.Add(ipText))
                continue;

            scanned++;

            // parsedIp permite consultar la MAC aprendida por ARP después de recibir la respuesta de discovery.
            IPAddress.TryParse(ipText, out var parsedIp);
            var mac = parsedIp is not null && arpTable.TryGetValue(parsedIp, out var knownMac)
                ? knownMac
                : vivotekDevice.MacAddress;

            // device conserva el objeto VIVOTEK y lo completa con evidencia ARP disponible.
            var device = vivotekDevice;
            device.MacAddress = mac;
            device.Manufacturer = "VIVOTEK";
            device.AssignedProviderName = "VIVOTEK";
            device.Status = DeviceStatus.Online;

            var update = new ScanProgress(scanned, total, device);
            progress?.Report(update);
            yield return update;
        }

        // Cuando ningún protocolo encontró un equipo, emitimos el cierre del escaneo.
        if (scanned == 0)
            yield return new ScanProgress(total, total, null);
    }
}
