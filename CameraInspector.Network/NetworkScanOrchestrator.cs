using System.Net;
using CameraInspector.Core.Interfaces;
using CameraInspector.Core.Models;
using CameraInspector.Network.Providers.Reolink;

namespace CameraInspector.Network;

/// <summary>
/// Orquestador de descubrimiento multi-evidencia. Todas las fuentes se fusionan por IP
/// antes de entregarlas a la UI para no perder información cuando varias técnicas detectan el mismo equipo.
/// </summary>
public sealed class NetworkScanOrchestrator : INetworkScanner
{
    private static readonly int[] DirectFastPorts =
    {
        80, 443, 554, 8000, 8080, 8554, 37777, 9000
    };

    private static readonly int[] DirectArpPorts =
    {
        80, 443, 554, 8080
    };

    private readonly ISubnetCalculator _subnetCalculator;
    private readonly IPingScanner _pingScanner;
    private readonly IArpResolver _arpResolver;
    private readonly IOnvifDiscoveryService _onvifDiscoveryService;
    private readonly IVivotekDiscoveryService _vivotekDiscoveryService;
    private readonly ReolinkDiscoveryService _reolinkDiscoveryService;
    private readonly CameraPortScanner _cameraPortScanner;
    private readonly SsdpDiscoveryService _ssdpDiscoveryService;
    private readonly LegacyVendorDiscoveryService _legacyVendorDiscoveryService;
    private readonly MdnsDiscoveryService _mdnsDiscoveryService;

    public NetworkScanOrchestrator(
        ISubnetCalculator subnetCalculator,
        IPingScanner pingScanner,
        IArpResolver arpResolver,
        IOnvifDiscoveryService onvifDiscoveryService,
        IVivotekDiscoveryService vivotekDiscoveryService,
        ReolinkDiscoveryService reolinkDiscoveryService,
        CameraPortScanner cameraPortScanner,
        SsdpDiscoveryService ssdpDiscoveryService,
        LegacyVendorDiscoveryService legacyVendorDiscoveryService,
        MdnsDiscoveryService mdnsDiscoveryService)
    {
        _subnetCalculator = subnetCalculator;
        _pingScanner = pingScanner;
        _arpResolver = arpResolver;
        _onvifDiscoveryService = onvifDiscoveryService;
        _vivotekDiscoveryService = vivotekDiscoveryService;
        _reolinkDiscoveryService = reolinkDiscoveryService;
        _cameraPortScanner = cameraPortScanner;
        _ssdpDiscoveryService = ssdpDiscoveryService;
        _legacyVendorDiscoveryService = legacyVendorDiscoveryService;
        _mdnsDiscoveryService = mdnsDiscoveryService;
    }

    public async IAsyncEnumerable<ScanProgress> ScanAsync(
        NetworkInterfaceInfo networkInterface,
        IProgress<ScanProgress>? progress = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default,
        DiscoveryScanMode mode = DiscoveryScanMode.NetworkSubnet,
        IPAddress? directAddress = null)
    {
        var directMode = mode == DiscoveryScanMode.DirectCamera;
        var hasDirectTarget = directMode && directAddress is not null;

        List<IPAddress> candidates;
        try
        {
            if (hasDirectTarget)
            {
                candidates = new List<IPAddress> { directAddress! };
            }
            else if (directMode)
            {
                // Sin IP no hacemos un sweep de subred. Discovery + ARP se utilizan como fuentes rápidas.
                candidates = new List<IPAddress>();
            }
            else
            {
                candidates = _subnetCalculator.GetHostAddresses(networkInterface).ToList();
            }
        }
        catch (InvalidOperationException)
        {
            candidates = hasDirectTarget ? new List<IPAddress> { directAddress! } : new List<IPAddress>();
        }

        var pingTask = candidates.Count == 0
            ? Task.FromResult<IReadOnlyList<IPAddress>>(Array.Empty<IPAddress>())
            : _pingScanner.ScanAsync(candidates, cancellationToken: cancellationToken);

        var onvifTask = SafeDiscoverAsync(() => _onvifDiscoveryService.DiscoverAsync(networkInterface, cancellationToken), cancellationToken);
        var vivotekTask = SafeDiscoverAsync(() => _vivotekDiscoveryService.DiscoverAsync(networkInterface, cancellationToken), cancellationToken);
        var reolinkTask = SafeDiscoverAsync(() => _reolinkDiscoveryService.DiscoverAsync(networkInterface, cancellationToken), cancellationToken);
        var ssdpTask = SafeDiscoverAsync(() => _ssdpDiscoveryService.DiscoverAsync(networkInterface, cancellationToken), cancellationToken);
        var legacyVendorTask = SafeDiscoverAsync(() => _legacyVendorDiscoveryService.DiscoverAsync(networkInterface, cancellationToken), cancellationToken);
        var mdnsTask = SafeDiscoverAsync(() => _mdnsDiscoveryService.DiscoverAsync(networkInterface, cancellationToken), cancellationToken);

        // Las fuentes siguen ejecutándose en paralelo. No añadimos esperas artificiales entre protocolos.
        await Task.WhenAll(pingTask, onvifTask, vivotekTask, reolinkTask, ssdpTask, legacyVendorTask, mdnsTask);

        var responsive = await pingTask;
        var onvifResults = await onvifTask;
        var vivotekResults = await vivotekTask;
        var reolinkResults = await reolinkTask;
        var ssdpResults = await ssdpTask;
        var legacyVendorResults = await legacyVendorTask;
        var mdnsResults = await mdnsTask;

        if (hasDirectTarget)
        {
            onvifResults = FilterDirect(onvifResults, directAddress!, GetIpFromOnvif);
            vivotekResults = FilterDirect(vivotekResults, directAddress!, ToIp);
            reolinkResults = FilterDirect(reolinkResults, directAddress!, ToIp);
            ssdpResults = FilterDirect(ssdpResults, directAddress!, ToIp);
            legacyVendorResults = FilterDirect(legacyVendorResults, directAddress!, ToIp);
            mdnsResults = FilterDirect(mdnsResults, directAddress!, ToIp);
            responsive = responsive.Where(ip => ip.Equals(directAddress)).ToArray();
        }

        // ARP es inmediato y evita el delay fijo de 150 ms que ralentizaba cada ejecución.
        var arpTable = _arpResolver.GetArpTable();

        var arpCandidates = hasDirectTarget
            ? arpTable.Keys.Where(ip => ip.Equals(directAddress!))
            : directMode
                ? arpTable.Keys
                : arpTable.Keys;

        var discoveryCandidates = onvifResults.Select(GetIpFromOnvif).Where(ip => ip is not null).Cast<IPAddress>()
            .Concat(vivotekResults.Select(ToIp).Where(ip => ip is not null).Cast<IPAddress>())
            .Concat(reolinkResults.Select(ToIp).Where(ip => ip is not null).Cast<IPAddress>())
            .Concat(ssdpResults.Select(ToIp).Where(ip => ip is not null).Cast<IPAddress>())
            .Concat(legacyVendorResults.Select(ToIp).Where(ip => ip is not null).Cast<IPAddress>())
            .Concat(mdnsResults.Select(ToIp).Where(ip => ip is not null).Cast<IPAddress>());

        var portCandidates = hasDirectTarget
            ? candidates
                .Concat(discoveryCandidates)
                .Where(address => address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                .Distinct()
                .ToList()
            : directMode
                ? discoveryCandidates
                    .Where(address => address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    .Distinct()
                    .ToList()
                : candidates
                    .Concat(arpCandidates)
                    .Concat(discoveryCandidates)
                    .Where(address => address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    .Distinct()
                    .ToList();

        IReadOnlyList<CameraPortScanResult> portResults;
        if (portCandidates.Count == 0 && directMode)
        {
            // No hubo discovery: usamos solo la tabla ARP y cuatro puertos de alta señal.
            // Sigue sin convertirse en un escaneo completo de subred.
            portResults = arpCandidates.Any()
                ? await _cameraPortScanner.ScanAsync(
                    arpCandidates,
                    timeoutMs: 120,
                    maxParallelism: 128,
                    cancellationToken: cancellationToken,
                    ports: DirectArpPorts)
                : Array.Empty<CameraPortScanResult>();
        }
        else
        {
            portResults = portCandidates.Count == 0
                ? Array.Empty<CameraPortScanResult>()
                : hasDirectTarget || directMode
                    ? await _cameraPortScanner.ScanAsync(
                        portCandidates,
                        timeoutMs: 150,
                        maxParallelism: 128,
                        cancellationToken: cancellationToken,
                        ports: DirectFastPorts)
                    : await _cameraPortScanner.ScanAsync(
                        portCandidates,
                        cancellationToken: cancellationToken);
        }

        var devices = new Dictionary<string, DiscoveredDevice>(StringComparer.OrdinalIgnoreCase);

        foreach (var ip in responsive)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var device = GetOrCreate(devices, ip.ToString());
            device.Status = DeviceStatus.Online;
            device.AddEvidence("Ping", 0.05, "ICMP respondió", false);
            if (arpTable.TryGetValue(ip, out var mac)) device.MacAddress ??= mac;
        }

        foreach (var result in onvifResults)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var ip = GetIpFromOnvif(result);
            if (ip is null) continue;
            var device = GetOrCreate(devices, ip.ToString());
            device.OnvifSupported = true;
            device.OnvifDeviceServiceXAddr = result.DeviceServiceXAddr;
            device.OnvifProfile = "detectado por WS-Discovery";
            device.CameraEvidence = true;
            device.Status = DeviceStatus.Online;
            device.AddEvidence("WS-Discovery", 0.98, "respuesta ONVIF", true);
            if (arpTable.TryGetValue(ip, out var mac)) device.MacAddress ??= mac;
        }

        MergeDiscoverySource(devices, vivotekResults, "VIVOTEK Discovery", true, 0.99, "Shepherd/IW2");
        MergeDiscoverySource(devices, reolinkResults, "Reolink LAN Discovery", true, 0.98, "UDP/2000 · respuesta UDP/3000");
        MergeDiscoverySource(devices, legacyVendorResults, null, true, 0.99, null);
        MergeDiscoverySource(devices, ssdpResults, "SSDP/UPnP", false, 0.35, null);
        MergeDiscoverySource(devices, mdnsResults, "mDNS/Bonjour", false, 0.3, null);

        foreach (var portResult in portResults)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var device = GetOrCreate(devices, portResult.IpAddress.ToString());
            if (arpTable.TryGetValue(portResult.IpAddress, out var mac)) device.MacAddress ??= mac;
            ApplyPortEvidence(device, portResult);
        }

        var ordered = devices.Values
            .Where(device => device.IpAddress.Length > 0)
            .Where(device => !hasDirectTarget || string.Equals(device.IpAddress, directAddress!.ToString(), StringComparison.OrdinalIgnoreCase))
            .OrderBy(device => device.IpAddress, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var total = Math.Max(candidates.Count, ordered.Count);
        for (var index = 0; index < ordered.Count; index++)
        {
            var device = ordered[index];
            cancellationToken.ThrowIfCancellationRequested();
            device.LastSeenAt = DateTimeOffset.UtcNow;
            yield return Report(new ScanProgress(index + 1, total, device), progress);
        }

        if (ordered.Count == 0)
            yield return new ScanProgress(total, total, null);
    }

    private static IReadOnlyList<T> FilterDirect<T>(
        IReadOnlyList<T> source,
        IPAddress target,
        Func<T, IPAddress?> ipSelector)
        => source.Where(item => ipSelector(item)?.Equals(target) == true).ToArray();

    private static DiscoveredDevice GetOrCreate(Dictionary<string, DiscoveredDevice> devices, string ipText)
    {
        if (!devices.TryGetValue(ipText, out var device))
        {
            device = new DiscoveredDevice { IpAddress = ipText };
            devices[ipText] = device;
        }
        return device;
    }

    private static void MergeDiscoverySource(
        Dictionary<string, DiscoveredDevice> devices,
        IEnumerable<DiscoveredDevice> sourceResults,
        string? evidenceMethod,
        bool defaultCameraEvidence,
        double confidence,
        string? defaultDetails)
    {
        foreach (var source in sourceResults)
        {
            if (!IPAddress.TryParse(source.IpAddress, out _)) continue;
            var target = GetOrCreate(devices, source.IpAddress);

            target.MacAddress ??= source.MacAddress;
            target.Hostname ??= source.Hostname;
            target.Manufacturer ??= source.Manufacturer;
            target.Model ??= source.Model;
            target.FirmwareVersion ??= source.FirmwareVersion;
            target.SerialNumber ??= source.SerialNumber;
            target.AssignedProviderName ??= source.AssignedProviderName;
            target.OnvifSupported |= source.OnvifSupported;
            target.RtspSupported |= source.RtspSupported;
            target.HttpSupported |= source.HttpSupported;
            target.HttpsSupported |= source.HttpsSupported;
            target.HttpPort ??= source.HttpPort;
            target.RtspPort ??= source.RtspPort;
            target.CameraEvidence |= source.CameraEvidence || defaultCameraEvidence;
            target.OnvifDeviceServiceXAddr ??= source.OnvifDeviceServiceXAddr;
            target.OnvifProfile ??= source.OnvifProfile;

            foreach (var evidence in source.DetectionEvidence)
                target.AddEvidence(evidence.Method, evidence.Confidence, evidence.Details, evidence.IsCameraEvidence);

            if (!string.IsNullOrWhiteSpace(evidenceMethod))
                target.AddEvidence(evidenceMethod, confidence, defaultDetails ?? source.AssignedProviderName ?? source.Manufacturer, defaultCameraEvidence);

            target.Status = DeviceStatus.Online;
        }
    }

    private static ScanProgress Report(ScanProgress update, IProgress<ScanProgress>? progress)
    {
        progress?.Report(update);
        return update;
    }

    private static async Task<IReadOnlyList<T>> SafeDiscoverAsync<T>(
        Func<Task<IReadOnlyList<T>>> operation,
        CancellationToken cancellationToken)
    {
        try { return await operation(); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch { return Array.Empty<T>(); }
    }

    private static IPAddress? GetIpFromOnvif(OnvifDiscoveryResult result)
    {
        if (!Uri.TryCreate(result.DeviceServiceXAddr, UriKind.Absolute, out var uri)) return null;
        return IPAddress.TryParse(uri.Host, out var ip) ? ip : null;
    }

    private static IPAddress? ToIp(DiscoveredDevice device)
        => IPAddress.TryParse(device.IpAddress, out var ip) ? ip : null;

    private static void ApplyPortEvidence(DiscoveredDevice device, CameraPortScanResult portResult)
    {
        device.HttpSupported |= portResult.Http;
        device.HttpsSupported |= portResult.Https;
        device.RtspSupported |= portResult.Rtsp;

        if (portResult.Ports.Contains(80)) device.HttpPort ??= 80;
        else if (portResult.Ports.Contains(81)) device.HttpPort ??= 81;
        else if (portResult.Ports.Contains(8080)) device.HttpPort ??= 8080;
        else if (portResult.Ports.Contains(8081)) device.HttpPort ??= 8081;
        else if (portResult.Ports.Contains(8888)) device.HttpPort ??= 8888;
        if (portResult.Ports.Contains(443) || portResult.Ports.Contains(8443)) device.HttpsSupported = true;
        if (portResult.Ports.Contains(554)) device.RtspPort ??= 554;
        else if (portResult.Ports.Contains(8554)) device.RtspPort ??= 8554;

        if (device.RtspSupported)
            device.AddEvidence("TCP/RTSP", 0.45, $"puerto RTSP abierto ({device.RtspPort ?? 554})", false);
        if (device.HttpSupported || device.HttpsSupported)
            device.AddEvidence("TCP/HTTP", 0.2, "servicio web abierto", false);
        device.Status = DeviceStatus.Online;
    }
}
