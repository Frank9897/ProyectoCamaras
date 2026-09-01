using System.Net;
using CameraInspector.Core.Interfaces;
using CameraInspector.Core.Models;

namespace CameraInspector.Network;

/// <summary>
/// Orquestador de descubrimiento multi-evidencia. ONVIF es una técnica más entre
/// mDNS, SSDP, protocolos propietarios, ARP, TCP e identificación RTSP/HTTP.
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

        var onvifTask = SafeDiscoverAsync(
            () => _onvifDiscoveryService.DiscoverAsync(networkInterface, cancellationToken), cancellationToken);
        var vivotekTask = SafeDiscoverAsync(
            () => _vivotekDiscoveryService.DiscoverAsync(networkInterface, cancellationToken), cancellationToken);
        var ssdpTask = SafeDiscoverAsync(
            () => _ssdpDiscoveryService.DiscoverAsync(networkInterface, cancellationToken), cancellationToken);
        var legacyVendorTask = SafeDiscoverAsync(
            () => _legacyVendorDiscoveryService.DiscoverAsync(networkInterface, cancellationToken), cancellationToken);
        var mdnsTask = SafeDiscoverAsync(
            () => _mdnsDiscoveryService.DiscoverAsync(networkInterface, cancellationToken), cancellationToken);

        await Task.WhenAll(pingTask, onvifTask, vivotekTask, ssdpTask, legacyVendorTask, mdnsTask);

        var responsive = await pingTask;
        var onvifResults = await onvifTask;
        var vivotekResults = await vivotekTask;
        var ssdpResults = await ssdpTask;
        var legacyVendorResults = await legacyVendorTask;
        var mdnsResults = await mdnsTask;

        await Task.Delay(150, cancellationToken);
        var arpTable = _arpResolver.GetArpTable();

        // TCP se ejecuta sobre IPs de la subred, vecinos ARP y cualquier dirección aportada por discovery.
        var portCandidates = candidates
            .Concat(arpTable.Keys)
            .Concat(onvifResults.Select(GetIpFromOnvif).Where(ip => ip is not null).Cast<IPAddress>())
            .Concat(vivotekResults.Select(item => IPAddress.TryParse(item.IpAddress, out var ip) ? ip : null).Where(ip => ip is not null).Cast<IPAddress>())
            .Concat(ssdpResults.Select(item => IPAddress.TryParse(item.IpAddress, out var ip) ? ip : null).Where(ip => ip is not null).Cast<IPAddress>())
            .Concat(legacyVendorResults.Select(item => IPAddress.TryParse(item.IpAddress, out var ip) ? ip : null).Where(ip => ip is not null).Cast<IPAddress>())
            .Concat(mdnsResults.Select(item => IPAddress.TryParse(item.IpAddress, out var ip) ? ip : null).Where(ip => ip is not null).Cast<IPAddress>())
            .Where(address => address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            .Distinct()
            .ToList();

        var portResults = portCandidates.Count == 0
            ? Array.Empty<CameraPortScanResult>()
            : await _cameraPortScanner.ScanAsync(portCandidates, cancellationToken: cancellationToken);

        var onvifByIp = onvifResults
            .Select(result => new
            {
                Result = result,
                Uri = Uri.TryCreate(result.DeviceServiceXAddr, UriKind.Absolute, out var parsedUri) ? parsedUri : null
            })
            .Where(item => item.Uri is not null && item.Uri.Host.Length > 0)
            .Select(item => new { item.Result, Address = item.Uri!.Host })
            .Where(item => IPAddress.TryParse(item.Address, out _))
            .GroupBy(item => item.Address, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Result, StringComparer.OrdinalIgnoreCase);

        var discoveredCount = responsive.Count + onvifByIp.Count + vivotekResults.Count + ssdpResults.Count +
                              legacyVendorResults.Count + mdnsResults.Count + portResults.Length;
        var total = Math.Max(candidates.Count, discoveredCount);
        var processedIps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var scanned = 0;

        foreach (var ip in responsive)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var ipText = ip.ToString();
            if (!processedIps.Add(ipText)) continue;

            arpTable.TryGetValue(ip, out var mac);
            var device = new DiscoveredDevice { IpAddress = ipText, MacAddress = mac, Status = DeviceStatus.Unknown };
            ApplyOnvifEvidence(device, onvifByIp, ipText);
            ApplyVivotekEvidence(device, vivotekResults, ipText);
            ApplyPortEvidence(device, portResults.FirstOrDefault(item => item.IpAddress.Equals(ip)));
            yield return Report(new ScanProgress(++scanned, total, device), progress);
        }

        foreach (var pair in onvifByIp)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!processedIps.Add(pair.Key)) continue;

            IPAddress.TryParse(pair.Key, out var parsedIp);
            var mac = parsedIp is not null && arpTable.TryGetValue(parsedIp, out var knownMac) ? knownMac : null;
            var device = new DiscoveredDevice
            {
                IpAddress = pair.Key, MacAddress = mac, Status = DeviceStatus.Online,
                OnvifSupported = true, OnvifProfile = "detectado por WS-Discovery",
                OnvifDeviceServiceXAddr = pair.Value.DeviceServiceXAddr
            };
            ApplyVivotekEvidence(device, vivotekResults, pair.Key);
            ApplyPortEvidence(device, parsedIp is not null ? portResults.FirstOrDefault(item => item.IpAddress.Equals(parsedIp)) : null);
            yield return Report(new ScanProgress(++scanned, total, device), progress);
        }

        foreach (var sourceDevice in ssdpResults.Concat(mdnsResults).Concat(legacyVendorResults).Concat(vivotekResults))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IPAddress.TryParse(sourceDevice.IpAddress, out var parsedIp) || !processedIps.Add(sourceDevice.IpAddress))
                continue;

            if (arpTable.TryGetValue(parsedIp, out var knownMac)) sourceDevice.MacAddress = knownMac;
            ApplyPortEvidence(sourceDevice, portResults.FirstOrDefault(item => item.IpAddress.Equals(parsedIp)));
            sourceDevice.Status = DeviceStatus.Online;
            yield return Report(new ScanProgress(++scanned, total, sourceDevice), progress);
        }

        foreach (var portResult in portResults)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var ipText = portResult.IpAddress.ToString();
            if (!processedIps.Add(ipText)) continue;

            arpTable.TryGetValue(portResult.IpAddress, out var mac);
            var device = new DiscoveredDevice { IpAddress = ipText, MacAddress = mac, Status = DeviceStatus.Online };
            ApplyPortEvidence(device, portResult);
            yield return Report(new ScanProgress(++scanned, total, device), progress);
        }

        if (scanned == 0)
            yield return new ScanProgress(total, total, null);
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

    private static void ApplyOnvifEvidence(DiscoveredDevice device, IReadOnlyDictionary<string, OnvifDiscoveryResult> onvifByIp, string ipText)
    {
        if (!onvifByIp.TryGetValue(ipText, out var result)) return;
        device.OnvifSupported = true;
        device.OnvifDeviceServiceXAddr = result.DeviceServiceXAddr;
        device.OnvifProfile = "detectado por WS-Discovery";
    }

    private static void ApplyVivotekEvidence(DiscoveredDevice device, IReadOnlyList<DiscoveredDevice> vivotekResults, string ipText)
    {
        var match = vivotekResults.FirstOrDefault(item => string.Equals(item.IpAddress, ipText, StringComparison.OrdinalIgnoreCase));
        if (match is null) return;
        device.Manufacturer = "VIVOTEK";
        device.AssignedProviderName = "VIVOTEK";
        device.Model ??= match.Model;
        device.MacAddress ??= match.MacAddress;
        device.CameraEvidence = true;
    }

    private static void ApplyPortEvidence(DiscoveredDevice device, CameraPortScanResult? portResult)
    {
        if (portResult is null) return;
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
        device.Status = DeviceStatus.Online;
    }
}
