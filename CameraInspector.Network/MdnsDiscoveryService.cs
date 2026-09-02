using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using CameraInspector.Core.Models;

namespace CameraInspector.Network;

/// <summary>
/// Descubrimiento mDNS/DNS-SD para cámaras y servicios de video.
/// Axis utiliza servicios _axis-video/VAPIX y otros fabricantes pueden anunciar HTTP/RTSP por Bonjour.
/// </summary>
public sealed class MdnsDiscoveryService
{
    private static readonly IPAddress MulticastAddress = IPAddress.Parse("224.0.0.251");
    private const int Port = 5353;
    private static readonly string[] ServiceTypes =
    {
        "_axis-video._tcp.local",
        "_vapix-http._tcp.local",
        "_vapix-https._tcp.local",
        "_http._tcp.local",
        "_https._tcp.local",
        "_rtsp._tcp.local"
    };

    private readonly TimeSpan _timeout = TimeSpan.FromSeconds(2.5);

    public async Task<IReadOnlyList<DiscoveredDevice>> DiscoverAsync(
        NetworkInterfaceInfo networkInterface,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(networkInterface);

        using var socket = new UdpClient(AddressFamily.InterNetwork);
        socket.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);

        try
        {
            socket.Client.Bind(new IPEndPoint(IPAddress.Any, Port));
            socket.Client.SetSocketOption(
                SocketOptionLevel.IP,
                SocketOptionName.AddMembership,
                new MulticastOption(MulticastAddress, networkInterface.IpAddress));
        }
        catch (SocketException)
        {
            // Algunos servicios mDNS de Windows ya ocupan UDP/5353. Intentar el puerto local
            // 5353 igualmente permite aprovechar respuestas unicast/de entorno disponibles.
            socket.Client.Bind(new IPEndPoint(networkInterface.IpAddress, 0));
        }

        try
        {
            socket.Client.SetSocketOption(
                SocketOptionLevel.IP,
                SocketOptionName.MulticastInterface,
                networkInterface.IpAddress.GetAddressBytes());
        }
        catch (SocketException)
        {
        }

        var result = new Dictionary<string, MdnsServiceRecord>(StringComparer.OrdinalIgnoreCase);
        foreach (var serviceType in ServiceTypes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var query = BuildPtrQuery(serviceType);
            try
            {
                await socket.SendAsync(query, query.Length, new IPEndPoint(MulticastAddress, Port));
            }
            catch (SocketException)
            {
            }
        }

        var deadline = DateTimeOffset.UtcNow + _timeout;
        while (!cancellationToken.IsCancellationRequested)
        {
            var remaining = deadline - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero) break;

            try
            {
                var receive = socket.ReceiveAsync(cancellationToken).AsTask();
                var timeout = Task.Delay(remaining, cancellationToken);
                if (await Task.WhenAny(receive, timeout) != receive) break;

                var packet = await receive;
                foreach (var record in ParsePacket(packet.Buffer))
                {
                    var key = record.InstanceName ?? record.TargetName ?? packet.RemoteEndPoint.Address.ToString();
                    result[key] = result.TryGetValue(key, out var existing)
                        ? Merge(existing, record, packet.RemoteEndPoint.Address)
                        : record with { SourceAddress = packet.RemoteEndPoint.Address };
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (SocketException)
            {
                break;
            }
        }

        return result.Values
            .Select(ToDevice)
            .Where(device => !string.IsNullOrWhiteSpace(device.IpAddress))
            .GroupBy(device => device.IpAddress, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
    }

    private static byte[] BuildPtrQuery(string serviceType)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        WriteUInt16Network(writer, 0);
        WriteUInt16Network(writer, 0);
        WriteUInt16Network(writer, 1);
        WriteUInt16Network(writer, 0);
        WriteUInt16Network(writer, 0);
        WriteUInt16Network(writer, 0);
        WriteDnsName(writer, serviceType);
        WriteUInt16Network(writer, 12);
        WriteUInt16Network(writer, 1);
        return stream.ToArray();
    }

    private static void WriteDnsName(BinaryWriter writer, string name)
    {
        foreach (var label in name.TrimEnd('.').Split('.'))
        {
            var bytes = Encoding.ASCII.GetBytes(label);
            writer.Write((byte)bytes.Length);
            writer.Write(bytes);
        }
        writer.Write((byte)0);
    }

    private static void WriteUInt16Network(BinaryWriter writer, ushort value)
    {
        writer.Write((byte)(value >> 8));
        writer.Write((byte)value);
    }

    private static IEnumerable<MdnsServiceRecord> ParsePacket(byte[] data)
    {
        if (data.Length < 12) yield break;
        var offset = 12;
        var questions = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(4, 2));
        var answers = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(6, 2));
        var authorities = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(8, 2));
        var additionals = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(10, 2));

        for (var i = 0; i < questions; i++)
        {
            if (!TryReadName(data, ref offset, out _)) yield break;
            if (!TrySkip(data, ref offset, 4)) yield break;
        }

        var records = new List<DnsRecord>();
        var totalRecords = answers + authorities + additionals;
        for (var i = 0; i < totalRecords; i++)
        {
            if (!TryReadRecord(data, ref offset, out var record)) yield break;
            records.Add(record);
        }

        var addresses = records
            .Where(r => r.Type == 1 && r.RDataLength == 4)
            .GroupBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => new IPAddress(data.Skip(g.Last().RDataOffsetInPacket).Take(4).ToArray()),
                StringComparer.OrdinalIgnoreCase);

        foreach (var ptr in records.Where(r => r.Type == 12))
        {
            var ptrOffset = ptr.RDataOffsetInPacket;
            if (!TryReadName(data, ref ptrOffset, out var instance)) continue;

            var srv = records.FirstOrDefault(r =>
                r.Type == 33 && r.Name.Equals(instance, StringComparison.OrdinalIgnoreCase));

            int? srvPort = null;
            string? target = null;
            if (srv.Type == 33)
                target = ReadSrvTarget(data, srv.RDataOffsetInPacket, out srvPort);

            var ip = target is not null && addresses.TryGetValue(target, out var parsed)
                ? parsed
                : null;
            var serviceType = ServiceTypes.FirstOrDefault(type =>
                ptr.Name.Equals(type, StringComparison.OrdinalIgnoreCase));
            if (serviceType is null) continue;

            yield return new MdnsServiceRecord(
                serviceType,
                instance,
                target,
                ip,
                srvPort,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        }
    }

    private static bool TryReadRecord(byte[] data, ref int offset, out DnsRecord record)
    {
        record = default;
        if (!TryReadName(data, ref offset, out var name)) return false;
        if (!TryReadUInt16(data, ref offset, out var type) ||
            !TryReadUInt16(data, ref offset, out var @class) ||
            !TrySkip(data, ref offset, 4) ||
            !TryReadUInt16(data, ref offset, out var length) ||
            offset + length > data.Length) return false;

        var rdataOffset = offset;
        offset += length;
        record = new DnsRecord(name, type, @class, rdataOffset, length);
        return true;
    }

    private static string? ReadSrvTarget(byte[] data, int offset, out int? port)
    {
        port = null;
        if (offset < 0 || offset + 6 > data.Length) return null;
        port = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(offset + 4, 2));
        var nameOffset = offset + 6;
        return TryReadName(data, ref nameOffset, out var target) ? target : null;
    }

    private static bool TryReadName(byte[] data, ref int offset, out string name)
    {
        name = string.Empty;
        var labels = new List<string>();
        var cursor = offset;
        var jumped = false;
        var safety = 0;

        while (cursor < data.Length && safety++ < 64)
        {
            var length = data[cursor++];
            if (length == 0)
            {
                if (!jumped) offset = cursor;
                name = string.Join('.', labels);
                return true;
            }

            if ((length & 0xC0) == 0xC0)
            {
                if (cursor >= data.Length) return false;
                var pointer = ((length & 0x3F) << 8) | data[cursor++];
                if (pointer >= data.Length) return false;
                if (!jumped) offset = cursor;
                cursor = pointer;
                jumped = true;
                continue;
            }

            if (length > 63 || cursor + length > data.Length) return false;
            labels.Add(Encoding.ASCII.GetString(data, cursor, length));
            cursor += length;
        }

        return false;
    }

    private static bool TryReadUInt16(byte[] data, ref int offset, out ushort value)
    {
        value = 0;
        if (offset + 2 > data.Length) return false;
        value = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(offset, 2));
        offset += 2;
        return true;
    }

    private static bool TrySkip(byte[] data, ref int offset, int count)
    {
        if (offset + count > data.Length) return false;
        offset += count;
        return true;
    }

    private static MdnsServiceRecord Merge(MdnsServiceRecord a, MdnsServiceRecord b, IPAddress source)
        => a with
        {
            TargetName = a.TargetName ?? b.TargetName,
            Address = a.Address ?? b.Address,
            Port = a.Port ?? b.Port,
            Txt = a.Txt.Count > 0 ? a.Txt : b.Txt,
            SourceAddress = a.SourceAddress ?? source
        };

    private static DiscoveredDevice ToDevice(MdnsServiceRecord record)
    {
        var isAxis = record.ServiceType.Equals("_axis-video._tcp.local", StringComparison.OrdinalIgnoreCase) ||
                     record.ServiceType.StartsWith("_vapix-", StringComparison.OrdinalIgnoreCase) ||
                     record.InstanceName?.Contains("Axis", StringComparison.OrdinalIgnoreCase) == true ||
                     record.TargetName?.Contains("axis", StringComparison.OrdinalIgnoreCase) == true;

        var isRtsp = record.ServiceType.Equals("_rtsp._tcp.local", StringComparison.OrdinalIgnoreCase);
        var isHttps = record.ServiceType.Contains("https", StringComparison.OrdinalIgnoreCase);
        var isHttp = record.ServiceType.Contains("http", StringComparison.OrdinalIgnoreCase) || isAxis;
        var provider = isAxis ? "Axis VAPIX / Bonjour" : "mDNS/Bonjour";

        var device = new DiscoveredDevice
        {
            IpAddress = record.Address?.ToString() ?? record.SourceAddress?.ToString() ?? string.Empty,
            Hostname = record.TargetName,
            Manufacturer = isAxis ? "Axis" : null,
            Model = isAxis ? ExtractModel(record.InstanceName) : null,
            CameraEvidence = isAxis || isRtsp,
            HttpSupported = isHttp,
            HttpsSupported = isHttps,
            HttpPort = !isRtsp ? record.Port : null,
            RtspSupported = isRtsp || isAxis,
            RtspPort = isRtsp ? record.Port : isAxis ? 554 : null,
            AssignedProviderName = provider,
            Status = DeviceStatus.Online
        };

        device.AddEvidence(
            "mDNS/Bonjour",
            isAxis || isRtsp ? 0.88 : 0.3,
            $"servicio {record.ServiceType}",
            isAxis || isRtsp);

        return device;
    }

    private static string? ExtractModel(string? instanceName)
    {
        if (string.IsNullOrWhiteSpace(instanceName)) return null;
        var clean = instanceName.TrimEnd('.');
        var dash = clean.IndexOf(" - ", StringComparison.Ordinal);
        return dash >= 0 ? clean[..dash] : clean;
    }

    private readonly record struct DnsRecord(string Name, ushort Type, ushort Class, int RDataOffsetInPacket, int RDataLength);
    private sealed record MdnsServiceRecord(
        string ServiceType,
        string? InstanceName,
        string? TargetName,
        IPAddress? Address,
        int? Port,
        Dictionary<string, string> Txt,
        IPAddress? SourceAddress = null);
}
