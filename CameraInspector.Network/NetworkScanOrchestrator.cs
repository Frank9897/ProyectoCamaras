using System.Net;
using CameraInspector.Core.Interfaces;
using CameraInspector.Core.Models;

namespace CameraInspector.Network;

/// <summary>
/// Orquestador principal del descubrimiento de dispositivos.
/// Combina ping/ARP, WS-Discovery, VIVOTEK, SSDP/UPnP y un sondeo TCP acotado
/// de puertos típicos de cámaras. El alcance puede ser directo, una subred o todas las interfaces.
/// </summary>
public sealed class NetworkScanOrchestrator : INetworkScanner
{
    private readonly ISubnetCalculator _subnetCalculator;
    private readonly IPingScanner _pingScanner;
    private readonly IArpResolver _arpResolver;
    private readonly IOnvifDiscoveryService _onvifDiscoveryService;
    private readonly IVivotekDiscoveryService _vivotekDiscoveryService;
    private readonly CameraPortScanner _cameraPortScanner;
    private readonly SsdpDiscoveryService _ssdpDiscoveryService;

    public NetworkScanOrchestrator(
        ISubnetCalculator subnetCalculator,
        IPingScanner pingScanner,
        IArpResolver arpResolver,
        IOnvifDiscoveryService onvifDiscoveryService,
        IVivotekDiscoveryService vivotekDiscoveryService,
        CameraPortScanner cameraPortScanner,
        SsdpDiscoveryService ssdpDiscoveryService)
    {
        _subnetCalculator = subnetCalculator;
        _pingScanner = pingScanner;
        _arpResolver = arpResolver;
        _onvifDiscoveryService = onvifDiscoveryService;
        _vivotekDiscoveryService = vivotekDiscoveryService;
        _cameraPortScanner = cameraPortScanner;
        _ssdpDiscoveryService = ssdpDiscoveryService;
    }

    public async IAsyncEnumerable<ScanProgress> ScanAsync(
        NetworkInterfaceInfo networkInterface,
        IProgress<ScanProgress>? progress = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default,
        DiscoveryScanMode mode = DiscoveryScanMode.NetworkSubnet)
    {
        List<IPAddress> candidates;

        try
        {
            candidates = mode == DiscoveryScanMode.DirectCamera
                ? new List<IPAddress>()
                : _subnetCalculator.GetHostAddresses(networkInterface).ToList();
        }
        catch (InvalidOperationException)
        {
            // Subred demasiado grande: omitimos únicamente el barrido IP y mantenemos discovery por protocolos.
            candidates = new List<IPAddress>();
        }

        var pingTask = candidates.Count == 0
            ? Task.FromResult<IReadOnlyList<IPAddress>>(Array.Empty<IPAddress>())
            : _pingScanner.ScanAsync(candidates, cancellationToken: cancellationToken);

        var onvifTask = _onvifDiscoveryService.DiscoverAsync(networkInterface, cancellationToken);
        var vivotekTask = _vivotekDiscoveryService.DiscoverAsync(networkInterface, cancellationToken);
        var ssdpTask = _ssdpDiscoveryService.DiscoverAsync(networkInterface, cancellationToken);

        await Task.WhenAll(pingTask, onvifTask, vivotekTask, ssdpTask);

        var responsive = await pingTask;
        var onvifResults = await onvifTask;
        var vivotekResults = await vivotekTask;
        var ssdpResults = await ssdpTask;

        // Esperamos a que el tráfico anterior tenga oportunidad de poblar la caché ARP.
        await Task.Delay(150, cancellationToken);
        var arpTable = _arpResolver.GetArpTable();

        // Para el sondeo TCP usamos IPs del sweep y también vecinos presentes en ARP.
        // Esto permite encontrar cámaras que ignoran ICMP pero que ya están visibles a nivel L2.
        var portCandidates = candidates
            .Concat(arpTable.Keys)
            .Where(address => address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            .Distinct()
            .ToList();

        var portTask = portCandidates.Count == 0
            ? Task.FromResult<IReadOnlyList<CameraPortScanResult>>(Array.Empty<CameraPortScanResult>())
            : _cameraPortScanner.ScanAsync(portCandidates, cancellationToken: cancellationToken);

        await portTask;
        var portResults = await portTask;

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

        var discoveredCount = responsive.Count +
                              onvifByIp.Count +
                              vivotekResults.Count +
                              ssdpResults.Count +
                              portResults.Count;
        var total = Math.Max(candidates.Count, discoveredCount);

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

            if (onvifByIp.TryGetValue(ipText, out var onvifResult))
            {
                device.OnvifSupported = true;
                device.OnvifDeviceServiceXAddr = onvifResult.DeviceServiceXAddr;
                device.OnvifProfile = "detectado por WS-Discovery";
            }

            if (vivotekResults.FirstOrDefault(item =>
                    string.Equals(item.IpAddress, ipText, StringComparison.OrdinalIgnoreCase)) is { } vivotekMatch)
            {
                device.Manufacturer = "VIVOTEK";
                device.AssignedProviderName = "VIVOTEK";
                device.Model = vivotekMatch.Model;
                device.MacAddress ??= vivotekMatch.MacAddress;
            }

            ApplyPortEvidence(device, portResults.FirstOrDefault(item => item.IpAddress.Equals(ip)));

            var update = new ScanProgress(scanned, total, device);
            progress?.Report(update);
            yield return update;
        }

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

            if (vivotekResults.Any(item => string.Equals(item.IpAddress, ipText, StringComparison.OrdinalIgnoreCase)))
            {
                device.Manufacturer = "VIVOTEK";
                device.AssignedProviderName = "VIVOTEK";
            }

            ApplyPortEvidence(device, portResults.FirstOrDefault(item => item.IpAddress.Equals(parsedIp)));

            var update = new ScanProgress(scanned, total, device);
            progress?.Report(update);
            yield return update;
        }

        foreach (var deviceFromSsdp in ssdpResults)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var ipText = deviceFromSsdp.IpAddress;

            if (!processedIps.Add(ipText))
                continue;

            scanned++;
            IPAddress.TryParse(ipText, out var parsedIp);

            if (parsedIp is not null && arpTable.TryGetValue(parsedIp, out var knownMac))
                deviceFromSsdp.MacAddress = knownMac;

            ApplyPortEvidence(deviceFromSsdp, portResults.FirstOrDefault(item => item.IpAddress.Equals(parsedIp)));

            var update = new ScanProgress(scanned, total, deviceFromSsdp);
            progress?.Report(update);
            yield return update;
        }

        foreach (var vivotekDevice in vivotekResults)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var ipText = vivotekDevice.IpAddress;

            if (!processedIps.Add(ipText))
                continue;

            scanned++;
            IPAddress.TryParse(ipText, out var parsedIp);

            if (parsedIp is not null && arpTable.TryGetValue(parsedIp, out var knownMac))
                vivotekDevice.MacAddress = knownMac;

            vivotekDevice.Manufacturer = "VIVOTEK";
            vivotekDevice.AssignedProviderName = "VIVOTEK";
            vivotekDevice.Status = DeviceStatus.Online;
            ApplyPortEvidence(vivotekDevice, portResults.FirstOrDefault(item => item.IpAddress.Equals(parsedIp)));

            var update = new ScanProgress(scanned, total, vivotekDevice);
            progress?.Report(update);
            yield return update;
        }

        // Último recurso: un host que tiene puertos típicos de cámara abiertos pero no respondió a
        // ping, ONVIF, SSDP ni discovery VIVOTEK también entra al inventario para ser investigado.
        foreach (var portResult in portResults)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var ipText = portResult.IpAddress.ToString();

            if (!processedIps.Add(ipText))
                continue;

            scanned++;
            arpTable.TryGetValue(portResult.IpAddress, out var mac);

            var device = new DiscoveredDevice
            {
                IpAddress = ipText,
                MacAddress = mac,
                Status = DeviceStatus.Online
            };
            ApplyPortEvidence(device, portResult);

            var update = new ScanProgress(scanned, total, device);
            progress?.Report(update);
            yield return update;
        }

        if (scanned == 0)
            yield return new ScanProgress(total, total, null);
    }

    private static void ApplyPortEvidence(
        DiscoveredDevice device,
        CameraPortScanResult? portResult)
    {
        if (portResult is null)
            return;

        device.HttpSupported |= portResult.Http;
        device.HttpsSupported |= portResult.Https;
        device.RtspSupported |= portResult.Rtsp;

        if (portResult.Ports.Contains(80))
            device.HttpPort ??= 80;
        else if (portResult.Ports.Contains(8080))
            device.HttpPort ??= 8080;
        else if (portResult.Ports.Contains(8081))
            device.HttpPort ??= 8081;
        else if (portResult.Ports.Contains(8888))
            device.HttpPort ??= 8888;

        if (portResult.Ports.Contains(443) && !device.HttpsSupported)
            device.HttpsSupported = true;

        if (portResult.Ports.Contains(554))
            device.RtspPort ??= 554;
        else if (portResult.Ports.Contains(8554))
            device.RtspPort ??= 8554;

        // Evidencia TCP de un servicio de cámara merece al menos estado Online,
        // aunque ICMP y ONVIF no respondan.
        device.Status = DeviceStatus.Online;
    }
}
