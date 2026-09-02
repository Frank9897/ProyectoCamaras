using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using CameraInspector.Core.Models;

namespace CameraInspector.Network;

/// <summary>
/// Descubrimiento propietario para fabricantes que no dependen de ONVIF.
/// Cubre SADP de Hikvision y DHIP de Dahua.
/// </summary>
public sealed class LegacyVendorDiscoveryService
{
    private static readonly IPAddress HikvisionMulticast = IPAddress.Parse("239.255.255.250");
    private static readonly IPAddress DahuaMulticast = IPAddress.Parse("239.255.255.251");

    public async Task<IReadOnlyList<DiscoveredDevice>> DiscoverAsync(
        NetworkInterfaceInfo networkInterface,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(networkInterface);

        var results = await Task.WhenAll(
            DiscoverHikvisionAsync(networkInterface, cancellationToken),
            DiscoverDahuaAsync(networkInterface, cancellationToken));

        return results.SelectMany(item => item)
            .GroupBy(item => item.IpAddress, StringComparer.OrdinalIgnoreCase)
            .Select(group => Merge(group))
            .ToList();
    }

    private static async Task<IReadOnlyList<DiscoveredDevice>> DiscoverHikvisionAsync(NetworkInterfaceInfo networkInterface, CancellationToken cancellationToken)
    {
        const int port = 37020;
        var results = new Dictionary<string, DiscoveredDevice>(StringComparer.OrdinalIgnoreCase);
        using var socket = CreateBoundSocket(networkInterface.IpAddress, port);
        socket.EnableBroadcast = true;
        var probe = Encoding.UTF8.GetBytes($"<?xml version=\"1.0\" encoding=\"utf-8\"?><Probe><Uuid>{Guid.NewGuid():D}</Uuid><Types>inquiry</Types></Probe>");

        foreach (var target in new[] { HikvisionMulticast, IPAddress.Broadcast })
        {
            try { await socket.SendAsync(probe, probe.Length, new IPEndPoint(target, port)); }
            catch (SocketException) { }
        }

        await ReceiveUntilAsync(socket, TimeSpan.FromSeconds(2.5), cancellationToken, packet =>
        {
            var parsed = ParseHikvisionResponse(packet.Buffer, packet.RemoteEndPoint.Address);
            if (parsed is not null) results[parsed.IpAddress] = parsed;
        });

        return results.Values.ToList();
    }

    private static async Task<IReadOnlyList<DiscoveredDevice>> DiscoverDahuaAsync(NetworkInterfaceInfo networkInterface, CancellationToken cancellationToken)
    {
        const int port = 37810;
        var results = new Dictionary<string, DiscoveredDevice>(StringComparer.OrdinalIgnoreCase);
        using var socket = new UdpClient(new IPEndPoint(networkInterface.IpAddress, 0)) { EnableBroadcast = true };
        var probe = BuildDahuaProbe();

        try
        {
            await socket.SendAsync(probe, probe.Length, new IPEndPoint(DahuaMulticast, port));
            await socket.SendAsync(probe, probe.Length, new IPEndPoint(IPAddress.Broadcast, port));
        }
        catch (SocketException) { }

        await ReceiveUntilAsync(socket, TimeSpan.FromSeconds(2.5), cancellationToken, packet =>
        {
            var parsed = ParseDahuaResponse(packet.Buffer, packet.RemoteEndPoint.Address);
            if (parsed is not null) results[parsed.IpAddress] = parsed;
        });

        return results.Values.ToList();
    }

    private static async Task ReceiveUntilAsync(UdpClient socket, TimeSpan timeout, CancellationToken cancellationToken, Action<UdpReceiveResult> handle)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (!cancellationToken.IsCancellationRequested)
        {
            var remaining = deadline - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero) break;

            try
            {
                var receive = socket.ReceiveAsync(cancellationToken).AsTask();
                var timer = Task.Delay(remaining, cancellationToken);
                if (await Task.WhenAny(receive, timer) != receive) break;
                handle(await receive);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (SocketException) { break; }
        }
    }

    private static UdpClient CreateBoundSocket(IPAddress address, int preferredPort)
    {
        try { return new UdpClient(new IPEndPoint(address, preferredPort)); }
        catch (SocketException) { return new UdpClient(new IPEndPoint(address, 0)); }
    }

    private static byte[] BuildDahuaProbe()
    {
        const uint headerSize = 32;
        const uint magic = 0x50494844;
        var json = Encoding.UTF8.GetBytes("{\"method\":\"DHDiscover.search\",\"params\":{\"mac\":\"\",\"uni\":1}}");
        var packet = new byte[32 + json.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(0, 4), headerSize);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(4, 4), magic);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(8, 4), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(12, 4), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(16, 4), (uint)json.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(20, 4), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(24, 4), (uint)json.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(28, 4), 0);
        json.CopyTo(packet, 32);
        return packet;
    }

    private static DiscoveredDevice? ParseHikvisionResponse(byte[] payload, IPAddress sourceAddress)
    {
        try
        {
            var text = Encoding.UTF8.GetString(payload);
            if (!text.Contains("ProbeMatch", StringComparison.OrdinalIgnoreCase) &&
                !text.Contains("Hikvision", StringComparison.OrdinalIgnoreCase) &&
                !text.Contains("Hik", StringComparison.OrdinalIgnoreCase)) return null;

            var document = XDocument.Parse(text, LoadOptions.PreserveWhitespace);
            var ip = ReadXml(document, "IPv4Address") ?? sourceAddress.ToString();
            if (!IPAddress.TryParse(ip, out _)) ip = sourceAddress.ToString();

            var device = new DiscoveredDevice
            {
                IpAddress = ip,
                MacAddress = NormalizeMac(ReadXml(document, "mac") ?? ReadXml(document, "MAC")),
                Manufacturer = "Hikvision",
                Model = ReadXml(document, "DeviceType") ?? ReadXml(document, "DeviceDescription"),
                FirmwareVersion = ReadXml(document, "SoftwareVersion"),
                SerialNumber = ReadXml(document, "SerialNumber"),
                HttpSupported = true,
                RtspSupported = true,
                HttpPort = TryParseInt(ReadXml(document, "HttpPort")) ?? 80,
                RtspPort = 554,
                AssignedProviderName = "Hikvision ISAPI",
                Status = DeviceStatus.Online
            };
            device.AddEvidence("Hikvision SADP", 0.99, "respuesta ProbeMatch", true);
            device.CameraEvidence = true;
            return device;
        }
        catch { return null; }
    }

    private static DiscoveredDevice? ParseDahuaResponse(byte[] payload, IPAddress sourceAddress)
    {
        try
        {
            const uint magic = 0x50494844;
            if (payload.Length < 32) return null;
            var actualMagic = BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(4, 4));
            var bodyOffset = actualMagic == magic ? 32 : 0;
            var body = Encoding.UTF8.GetString(payload, bodyOffset, payload.Length - bodyOffset);
            using var document = JsonDocument.Parse(body);

            JsonElement info;
            if (document.RootElement.TryGetProperty("params", out var parameters) && parameters.TryGetProperty("deviceInfo", out var deviceInfo)) info = deviceInfo;
            else if (document.RootElement.TryGetProperty("params", out parameters)) info = parameters;
            else return null;

            var ip = GetJsonString(info, "IP") ?? GetJsonString(info, "ip") ?? sourceAddress.ToString();
            if (!IPAddress.TryParse(ip, out _)) ip = sourceAddress.ToString();

            var device = new DiscoveredDevice
            {
                IpAddress = ip,
                MacAddress = NormalizeMac(GetJsonString(info, "mac") ?? GetJsonString(info, "MAC")),
                Manufacturer = "Dahua",
                Model = GetJsonString(info, "DeviceType") ?? GetJsonString(info, "deviceType"),
                SerialNumber = GetJsonString(info, "SerialNo") ?? GetJsonString(info, "serialNumber"),
                HttpSupported = true,
                RtspSupported = true,
                HttpPort = GetJsonInt(info, "HttpPort") ?? 80,
                RtspPort = 554,
                AssignedProviderName = "Dahua CGI/DHIP",
                Status = DeviceStatus.Online
            };
            device.AddEvidence("Dahua DHIP", 0.99, "respuesta DHDiscover.search", true);
            device.CameraEvidence = true;
            return device;
        }
        catch { return null; }
    }

    private static DiscoveredDevice Merge(IEnumerable<DiscoveredDevice> devices)
    {
        var ordered = devices.ToList();
        var first = ordered[0];
        foreach (var item in ordered.Skip(1))
        {
            first.MacAddress ??= item.MacAddress;
            first.Model ??= item.Model;
            first.FirmwareVersion ??= item.FirmwareVersion;
            first.SerialNumber ??= item.SerialNumber;
            first.AssignedProviderName ??= item.AssignedProviderName;
            first.HttpSupported |= item.HttpSupported;
            first.HttpsSupported |= item.HttpsSupported;
            first.RtspSupported |= item.RtspSupported;
            first.HttpPort ??= item.HttpPort;
            first.RtspPort ??= item.RtspPort;
            first.CameraEvidence |= item.CameraEvidence;
            foreach (var evidence in item.DetectionEvidence)
                first.AddEvidence(evidence.Method, evidence.Confidence, evidence.Details, evidence.IsCameraEvidence);
        }
        return first;
    }

    private static string? ReadXml(XDocument document, string localName) => document.Descendants().FirstOrDefault(element => element.Name.LocalName.Equals(localName, StringComparison.OrdinalIgnoreCase))?.Value.Trim();
    private static string? GetJsonString(JsonElement element, string name) => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static int? GetJsonInt(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value)) return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)) return number;
        return int.TryParse(value.ToString(), out var parsed) ? parsed : null;
    }
    private static int? TryParseInt(string? value) => int.TryParse(value, out var result) ? result : null;
    private static string? NormalizeMac(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var compact = value.Replace(":", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal).Trim();
        if (compact.Length != 12 || compact.Any(character => !Uri.IsHexDigit(character))) return null;
        return string.Join(":", Enumerable.Range(0, 6).Select(index => compact.Substring(index * 2, 2).ToUpperInvariant()));
    }
}
