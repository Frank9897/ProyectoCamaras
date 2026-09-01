using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using CameraInspector.Core.Models;

namespace CameraInspector.Network;

/// <summary>
/// Descubrimiento mDNS/DNS-SD para servicios de vídeo de Axis.
/// Se limita a tipos de servicio específicos para evitar convertir _http._tcp en ruido de red.
/// </summary>
public sealed class MdnsDiscoveryService
{
    private static readonly IPAddress MulticastAddress = IPAddress.Parse("224.0.0.251");
    private const int Port = 5353;
    private static readonly string[] ServiceTypes =
    {
        "_axis-video._tcp.local",
        "_axis_vapix._tcp.local",
        "_axis-vapix-http._tcp.local",
        "_axis-vapix-https._tcp.local"
    };

    private readonly TimeSpan _timeout = TimeSpan.FromSeconds(2.5);

    public async Task<IReadOnlyList<DiscoveredDevice>> DiscoverAsync(
        NetworkInterfaceInfo networkInterface,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(networkInterface);

        using var socket = new UdpClient(AddressFamily.InterNetwork);
        socket.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        socket.Client.Bind(new IPEndPoint(networkInterface.IpAddress, 0));
        socket.Client.SetSocketOption(
            SocketOptionLevel.IP,
            SocketOptionName.MulticastInterface,
            networkInterface.IpAddress.GetAddressBytes());

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
                // Una consulta mDNS concreta puede no estar permitida por la NIC.
            }
        }

        var deadline = DateTimeOffset.UtcNow + _timeout;
        while (!cancellationToken.IsCancellationRequested)
        {
            var remaining = deadline - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero)
                break;

            try
            {
                var receive = socket.ReceiveAsync(cancellationToken).AsTask();
                var timeout = Task.Delay(remaining, cancellationToken);
                if (await Task.WhenAny(receive, timeout) != receive)
                    break;

                var packet = await receive;
                foreach (var record in ParsePacket(packet.Buffer))
                {
                    if (!ServiceTypes.Any(type => string.Equals(type, record.ServiceType, StringComparison.OrdinalIgnoreCase)))
                        continue;

                    var key = record.InstanceName ?? record.TargetName ?? packet.RemoteEndPoint.Address.ToString();
                    if (!result.TryGetValue(key, out var existing))
                    {
                        result[key] = record with { SourceAddress = packet.RemoteEndPoint.Address };
                    }
                    else
                    {
                        result[key] = Merge(existing, record, packet.RemoteEndPoint.Address);
                    }
                }
            }
            catch (OperationCanceledException)
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
            .Where(device => device is not null)
            .Cast<DiscoveredDevice>()
            .GroupBy(device => device.IpAddress, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
    }

    private static byte[] BuildPtrQuery(string serviceType)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        writer.Write((ushort)0);
        writer.Write((ushort)0);
        writer.Write((ushort)1);
        writer.Write((ushort)0);
        writer.Write((ushort)0);
        writer.Write((ushort)0);
        WriteDnsName(writer, serviceType);
        WriteUInt16Network(writer, 12); // PTR
        WriteUInt16Network(writer, 1);  // IN
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
        writer.Write((byte)(value & 0xFF));
    }

    private static IEnumerable<MdnsServiceRecord> ParsePacket(byte[] data)
    {
        if (data.Length < 12)
            yield break;

        var offset = 12;
        var questionCount = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(4, 2));
        var answerCount = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(6, 2));
        var authorityCount = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(8, 2));
        var additionalCount = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(10, 2));

        for (var index = 0; index < questionCount; index++)
        {
            if (!TryReadName(data, ref offset, out _)) yield break;
            if (!TrySkip(data, ref offset, 4)) yield break;
        }

        var totalRecords = answerCount + authorityCount + additionalCount;
        var records = new List<DnsRecord>();
        for (var index = 0; index < totalRecords; index++)
        {
            if (!TryReadRecord(data, ref offset, out var record))
                yield break;
            records.Add(record);
        }

        var addresses = records
            .Where(record => record.Type == 1 && record.RData.Length == 4)
            .ToDictionary(record => record.Name, record => new IPAddress(record.RData), StringComparer.OrdinalIgnoreCase);

        var serviceTypes = new HashSet<string>(ServiceTypes, StringComparer.OrdinalIgnoreCase);
        foreach (var ptr in records.Where(record => record.Type == 12))
        {
            if (!TryReadNameFromBytes(ptr.RData, out var instance))
                continue;

            var srv = records.FirstOrDefault(record => record.Type == 33 && record.Name.Equals(instance, StringComparison.OrdinalIgnoreCase));
            var target = srv is null ? instance : ReadSrvTarget(srv.RData, out var srvPort);
            var ip = target is not null && addresses.TryGetValue(target, out var targetIp) ? targetIp : null;
            var serviceType = InferServiceType(ptr.Name, serviceTypes, instance);
            if (serviceType is null)
                continue;

            var txt = records.FirstOrDefault(record => record.Type == 16 && record.Name.Equals(instance, StringComparison.OrdinalIgnoreCase));
            yield return new MdnsServiceRecord(
                serviceType,
                instance,
                target,
                ip,
                srvPort,
                ParseTxt(txt?.RData));
        }
    }

    private static bool TryReadRecord(byte[] data, ref int offset, out DnsRecord record)
    {
        record = default;
        if (!TryReadName(data, ref offset, out var name))
            return false;
        if (!TryReadUInt16(data, ref offset, out var type) ||
            !TryReadUInt16(data, ref offset, out var @class) ||
            !TrySkip(data, ref offset, 4) ||
            !TryReadUInt16(data, ref offset, out var length) ||
            offset + length > data.Length)
            return false;

        var rdata = data[offset..(offset + length)];
        offset += length;
        record = new DnsRecord(name, type, @class, rdata);
        return true;
    }

    private static string? InferServiceType(string name, IEnumerable<string> serviceTypes, string instance)
    {
        foreach (var type in serviceTypes)
        {
            var prefix = instance.EndsWith(type, StringComparison.OrdinalIgnoreCase) ? type :
                name.EndsWith(type, StringComparison.OrdinalIgnoreCase) ? type : null;
            if (prefix is not null) return prefix;
        }
        return serviceTypes.FirstOrDefault(type => instance.Contains("axis", StringComparison.OrdinalIgnoreCase));
    }

    private static string? ReadSrvTarget(byte[] rdata, out int? port)
    {
        port = null;
        if (rdata.Length < 6)
            return null;
        port = BinaryPrimitives.ReadUInt16BigEndian(rdata.AsSpan(4, 2));
        if (!TryReadNameFromBytes(rdata[6..], out var target))
        {
            // DNS compression should not appear inside standalone SRV RDATA in most mDNS responses.
            return null;
        }
        return target;
    }

    private static Dictionary<string, string> ParseTxt(byte[]? rdata)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (rdata is null) return values;
        var offset = 0;
        while (offset < rdata.Length)
        {
            var length = rdata[offset++];
            if (length == 0 || offset + length > rdata.Length) break;
            var text = Encoding.UTF8.GetString(rdata, offset, length);
            offset += length;
            var separator = text.IndexOf('=');
            if (separator > 0)
                values[text[..separator]] = text[(separator + 1)..];
        }
        return values;
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

            if (length > 63 || cursor + length > data.Length)
                return false;
            labels.Add(Encoding.ASCII.GetString(data, cursor, length));
            cursor += length;
        }
        return false;
    }

    private static bool TryReadNameFromBytes(byte[] data, out string name)
    {
        var offset = 0;
        return TryReadName(data, ref offset, out name);
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
            Address = a.Address ?? b.Address ?? source,
            Port = a.Port ?? b.Port,
            Txt = a.Txt.Count > 0 ? a.Txt : b.Txt
        };

    private static DiscoveredDevice? ToDevice(MdnsServiceRecord record)
    {
        if (record.Address is null)
            return null;

        var isHttps = record.ServiceType.Contains("https", StringComparison.OrdinalIgnoreCase);
        return new DiscoveredDevice
        {
            IpAddress = record.Address.ToString(),
            Hostname = record.TargetName,
            Manufacturer = "Axis",
            Model = ExtractModel(record.InstanceName),
            HttpSupported = !isHttps,
            HttpsSupported = isHttps,
            HttpPort = !isHttps ? record.Port : null,
            RtspSupported = true,
            RtspPort = 554,
            AssignedProviderName = "Axis VAPIX",
            Status = DeviceStatus.Online
        };
    }

    private static string? ExtractModel(string? instanceName)
    {
        if (string.IsNullOrWhiteSpace(instanceName)) return null;
        var clean = instanceName.TrimEnd('.');
        var dash = clean.IndexOf(" - ", StringComparison.Ordinal);
        return dash >= 0 ? clean[..dash] : clean;
    }

    private readonly record struct DnsRecord(string Name, ushort Type, ushort Class, byte[] RData);

    private sealed record MdnsServiceRecord(
        string ServiceType,
        string? InstanceName,
        string? TargetName,
        IPAddress? Address,
        int? Port,
        Dictionary<string, string> Txt,
        IPAddress? SourceAddress = null);
}
