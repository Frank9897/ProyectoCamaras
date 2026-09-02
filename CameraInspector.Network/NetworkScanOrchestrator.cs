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

        // Todos los protocolos arrancan juntos, pero la UI recibe cada lote apenas termina.
        var discoveryTasks = new List<Task<IReadOnlyList<DiscoveredDevice>>>
        {
            DiscoverOnvifAsync(networkInterface, cancellationToken),
            SafeDiscoverAsync(() => _vivotekDiscoveryService.DiscoverAsync(networkInterface, cancellationToken), cancellationToken),
            SafeDiscoverAsync(() => _reolinkDiscoveryService.DiscoverAsync(networkInterface, cancellationToken), cancellationToken),
            SafeDiscoverAsync(() => _ssdpDiscoveryService.DiscoverAsync(networkInterface, cancellationToken), cancellationToken),
            SafeDiscoverAsync(() => _legacyVendorDiscoveryService.DiscoverAsync(networkInterface, cancellationToken), cancellationToken),
            SafeDiscoverAsync(() => _mdnsDiscoveryService.DiscoverAsync(networkInterface, cancellationToken), cancellationToken)
        };

        var arpTable = _arpResolver.GetArpTable();
        var devices = new Dictionary<string, DiscoveredDevice>(StringComparer.OrdinalIgnoreCase);
        var emittedIps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var discoveredCount = 0;

        // Ping no necesita bloquear al discovery. En modo directo con IP solo interesa el objetivo.
        var responsive = await pingTask;
        foreach (var ip in responsive)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (hasDirectTarget && !ip.Equals(directAddress)) continue;

            var device = GetOrCreate(devices, ip.ToString());
            device.Status = DeviceStatus.Online;
            device.AddEvidence("Ping", 0.05, "ICMP respondió", false);
            if (arpTable.TryGetValue(ip, out var mac)) device.MacAddress ??= mac;
        }

        // Esperamos a que cada protocolo termine por separado y publicamos inmediatamente.
        var pending = discoveryTasks.ToList();
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var completed = await Task.WhenAny(pending);
            pending.Remove(completed);
            var batch = await completed;

            foreach (var source in batch)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!IPAddress.TryParse(source.IpAddress, out var ip)) continue;
                if (hasDirectTarget && !ip.Equals(directAddress)) continue;

                var device = GetOrCreate(devices, ip.ToString());
                MergeSourceIntoDevice(device, source);
                if (arpTable.TryGetValue(ip, out var mac)) device.MacAddress ??= mac;

                if (!emittedIps.Add(device.IpAddress))
                    continue;

                device.LastSeenAt = DateTimeOffset.UtcNow;
                discoveredCount++;
                yield return Report(new ScanProgress(discoveredCount, 0, device), progress);
            }
        }

        // ARP aporta candidatos para cámaras que no contestan discovery.
        var arpCandidates = hasDirectTarget
            ? arpTable.Keys.Where(ip => ip.Equals(directAddress!))
            : arpTable.Keys;

        foreach (var ip in arpCandidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!emittedIps.Add(ip.ToString()))
                continue;

            var device = GetOrCreate(devices, ip.ToString());
            device.Status = DeviceStatus.Online;
            if (arpTable.TryGetValue(ip, out var mac)) device.MacAddress ??= mac;
            device.AddEvidence("ARP", 0.08, "host presente en caché ARP", false);

            if (directMode)
            {
                device.LastSeenAt = DateTimeOffset.UtcNow;
                discoveredCount++;
                yield return Report(new ScanProgress(discoveredCount, 0, device), progress);
            }
        }

        var discoveryCandidates = devices.Values
            .Select(device => IPAddress.TryParse(device.IpAddress, out var ip) ? ip : null)
            .Where(ip => ip is not null)
            .Cast<IPAddress>()
            .Distinct()
            .ToList();

        var portCandidates = hasDirectTarget
            ? discoveryCandidates
                .Append(directAddress!)
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

        foreach (var portResult in portResults)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var device = GetOrCreate(devices, portResult.IpAddress.ToString());
            if (arpTable.TryGetValue(portResult.IpAddress, out var mac)) device.MacAddress ??= mac;
            ApplyPortEvidence(device, portResult);

            // Un puerto puede ser la primera evidencia fuerte de una cámara.
            if (!emittedIps.Add(device.IpAddress))
                continue;

            device.LastSeenAt = DateTimeOffset.UtcNow;
            discoveredCount++;
            yield return Report(new ScanProgress(discoveredCount, 0, device), progress);
        }

        if (discoveredCount == 0)
            yield return new ScanProgress(0, Math.Max(candidates.Count, 0), null);
    }

    private async Task<IReadOnlyList<DiscoveredDevice>> DiscoverOnvifAsync(
        NetworkInterfaceInfo networkInterface,
        CancellationToken cancellationToken)
    {
        var results = await SafeDiscoverAsync(
            () => _onvifDiscoveryService.DiscoverAsync(networkInterface, cancellationToken),
            cancellationToken);

        var devices = new List<DiscoveredDevice>(results.Count);
        foreach (var result in results)
        {
            var ip = GetIpFromOnvif(result);
            if (ip is null) continue;

            var device = new DiscoveredDevice
            {
                IpAddress = ip.ToString(),
                OnvifSupported = true,
                OnvifDeviceServiceXAddr = result.DeviceServiceXAddr,
                OnvifProfile = "detectado por WS-Discovery",
                CameraEvidence = true,
                Status = DeviceStatus.Online
            };
            device.AddEvidence("WS-Discovery", 0.98, "respuesta ONVIF", true);
            devices.Add(device);
        }
        return devices;
    }

    private static void MergeSourceIntoDevice(DiscoveredDevice target, DiscoveredDevice source)
    {
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
        target.CameraEvidence |= source.CameraEvidence;
        target.OnvifDeviceServiceXAddr ??= source.OnvifDeviceServiceXAddr;
        target.OnvifProfile ??= source.OnvifProfile;

        foreach (var evidence in source.DetectionEvidence)
            target.AddEvidence(evidence.Method, evidence.Confidence, evidence.Details, evidence.IsCameraEvidence);

        target.Status = DeviceStatus.Online;
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
