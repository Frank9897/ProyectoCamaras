using System.Net;
using CameraInspector.Core.Interfaces;
using CameraInspector.Core.Models;

namespace CameraInspector.Network;

/// <summary>
/// Orquestador de descubrimiento multi-evidencia. Todas las fuentes se fusionan por IP
/// antes de entregarlas a la UI para no perder información cuando varias técnicas detectan el mismo equipo.
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
    private readonly LegacyVendorDiscoveryService _legacyVendorDiscoveryService;
    private readonly MdnsDiscoveryService _mdnsDiscoveryService;

    public NetworkScanOrchestrator(
        ISubnetCalculator subnetCalculator,
        IPingScanner pingScanner,
        IArpResolver arpResolver,
        IOnvifDiscoveryService onvifDiscoveryService,
        IVivotekDiscoveryService vivotekDiscoveryService,
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
        _cameraPortScanner = cameraPortScanner;
        _ssdpDiscoveryService = ssdpDiscoveryService;
        _legacyVendorDiscoveryService = legacyVendorDiscoveryService;
        _mdnsDiscoveryService = mdnsDiscoveryService;
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
            var directSmallSubnet = mode == DiscoveryScanMode.DirectCamera && networkInterface.CidrPrefixLength >= 23;
            candidates = mode == DiscoveryScanMode.DirectCamera && !directSmallSubnet
                ? new List<IPAddress>()
                : _subnetCalculator.GetHostAddresses(networkInterface).ToList();
        }
        catch (InvalidOperationException)
        {
            candidates = new List<IPAddress>();
        }

        var pingTask = candidates.Count == 0
            ? Task.FromResult<IReadOnlyList<IPAddress>>(Array.Empty<IPAddress>())
            : _pingScanner.ScanAsync(candidates, cancellationToken: cancellationToken);

        var onvifTask = SafeDiscoverAsync(() => _onvifDiscoveryService.DiscoverAsync(networkInterface, cancellationToken), cancellationToken);
        var vivotekTask = SafeDiscoverAsync(() => _vivotekDiscoveryService.DiscoverAsync(networkInterface, cancellationToken), cancellationToken);
        var ssdpTask = SafeDiscoverAsync(() => _ssdpDiscoveryService.DiscoverAsync(networkInterface, cancellationToken), cancellationToken);
        var legacyVendorTask = SafeDiscoverAsync(() => _legacyVendorDiscoveryService.DiscoverAsync(networkInterface, cancellationToken), cancellationToken);
        var mdnsTask = SafeDiscoverAsync(() => _mdnsDiscoveryService.DiscoverAsync(networkInterface, cancellationToken), cancellationToken);

        await Task.WhenAll(pingTask, onvifTask, vivotekTask, ssdpTask, legacyVendorTask, mdnsTask);

        var responsive = await pingTask;
        var onvifResults = await onvifTask;
        var vivotekResults = await vivotekTask;
        var ssdpResults = await ssdpTask;
        var legacyVendorResults = await legacyVendorTask;
        var mdnsResults = await mdnsTask;

        await Task.Delay(150, cancellationToken);
        var arpTable = _arpResolver.GetArpTable();

        var portCandidates = candidates
            .Concat(arpTable.Keys)
            .Concat(onvifResults.Select(GetIpFromOnvif).Where(ip => ip is not null).Cast<IPAddress>())
            .Concat(vivotekResults.Select(ToIp).Where(ip => ip is not null).Cast<IPAddress>())
            .Concat(ssdpResults.Select(ToIp).Where(ip => ip is not null).Cast<IPAddress>())
            .Concat(legacyVendorResults.Select(ToIp).Where(ip => ip is not null).Cast<IPAddress>())
            .Concat(mdnsResults.Select(ToIp).Where(ip => ip is not null).Cast<IPAddress>())
            .Where(address => address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            .Distinct()
            .ToList();

        var portResults = portCandidates.Count == 0
            ? Array.Empty<CameraPortScanResult>()
            : await _cameraPortScanner.ScanAsync(portCandidates, cancellationToken: cancellationToken);

        var devices = new Dictionary<string, DiscoveredDevice>(StringComparer.OrdinalIgnoreCase);

        foreach (var ip in responsive)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var ipText = ip.ToString();
            var device = GetOrCreate(devices, ipText);
            if (arpTable.TryGetValue(ip, out var mac)) device.MacAddress ??= mac;
            device.Status = DeviceStatus.Online;
            device.AddEvidence("Ping", 0.05, "ICMP respondió", false);
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

        MergeDiscoverySource(devices, vivotekResults, "VIVOTEK Discovery", true, 0.99, "Shepherd/IW2", "VIVOTEK");
        MergeDiscoverySource(devices, legacyVendorResults, null, true, 0.99, null, null);
        MergeDiscoverySource(devices, ssdpResults, "SSDP/UPnP", false, 0.35, null, null);
        MergeDiscoverySource(devices, mdnsResults, "mDNS/Bonjour", false, 0.3, null, null);

        foreach (var portResult in portResults)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var ipText = portResult.IpAddress.ToString();
            var device = GetOrCreate(devices, ipText);
            if (arpTable.TryGetValue(portResult.IpAddress, out var mac)) device.MacAddress ??= mac;
            ApplyPortEvidence(device, portResult);
        }

        var ordered = devices.Values
            .Where(device => device.IpAddress.Length > 0)
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
        string? defaultDetails,
        string? defaultManufacturer)
    {
        foreach (var source in sourceResults)
        {
            if (!IPAddress.TryParse(source.IpAddress, out var ip)) continue;
            var target = GetOrCreate(devices, source.IpAddress);

            target.MacAddress ??= source.MacAddress;
            target.Hostname ??= source.Hostname;
            target.Manufacturer ??= source.Manufacturer ?? defaultManufacturer;
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
            if (!string.IsNullOrWhiteSpace(source.OnvifDeviceServiceXAddr)) target.OnvifDeviceServiceXAddr ??= source.OnvifDeviceServiceXAddr;
            if (!string.IsNullOrWhiteSpace(source.OnvifProfile)) target.OnvifProfile ??= source.OnvifProfile;
            if (source.DetectionEvidence.Count > 0)
            {
                foreach (var evidence in source.DetectionEvidence)
                    target.AddEvidence(evidence.Method, evidence.Confidence, evidence.Details, evidence.IsCameraEvidence);
            }

            if (!string.IsNullOrWhiteSpace(evidenceMethod))
                target.AddEvidence(evidenceMethod, confidence, defaultDetails ?? source.AssignedProviderName ?? source.Manufacturer, defaultCameraEvidence);
            target.Status = DeviceStatus.Online;

            if (target.MacAddress is null && arpTablePlaceholder())
            {
                // Se conserva el flujo de fusión; la MAC se completa en la capa de ARP si está disponible.
            }
        }

        static bool arpTablePlaceholder() => false;
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
