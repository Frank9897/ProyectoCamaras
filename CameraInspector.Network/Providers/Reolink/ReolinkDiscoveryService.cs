using System.Net;
using System.Net.Sockets;
using System.Text;
using CameraInspector.Core.Models;

namespace CameraInspector.Network.Providers.Reolink;

/// <summary>
/// Descubrimiento LAN de Reolink mediante broadcast UDP.
/// El cliente Reolink utiliza el comando binario aaaa0000 hacia UDP/2000 y
/// recibe las respuestas en UDP/3000. No requiere ONVIF ni autenticación.
/// </summary>
public sealed class ReolinkDiscoveryService
{
    private static readonly byte[] Probe = { 0xAA, 0xAA, 0x00, 0x00 };
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(1.2);

    public async Task<IReadOnlyList<DiscoveredDevice>> DiscoverAsync(
        NetworkInterfaceInfo networkInterface,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(networkInterface);

        using var socket = CreateSocket(networkInterface.IpAddress);
        socket.EnableBroadcast = true;

        var destinations = new[]
        {
            CalculateBroadcastAddress(networkInterface.IpAddress, networkInterface.SubnetMask),
            IPAddress.Broadcast
        }.Distinct();

        foreach (var destination in destinations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await socket.SendAsync(Probe, Probe.Length, new IPEndPoint(destination, 2000));
            }
            catch (SocketException)
            {
            }
        }

        var results = new Dictionary<string, DiscoveredDevice>(StringComparer.OrdinalIgnoreCase);
        var deadline = DateTimeOffset.UtcNow + Timeout;

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
                if (!LooksLikeReolinkReply(packet.Buffer))
                    continue;

                var device = ParseResponse(packet.Buffer, packet.RemoteEndPoint.Address);
                results[device.IpAddress] = device;
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

        return results.Values.ToList();
    }

    private static UdpClient CreateSocket(IPAddress bindAddress)
    {
        try
        {
            return new UdpClient(new IPEndPoint(bindAddress, 3000));
        }
        catch (SocketException)
        {
            return new UdpClient(new IPEndPoint(bindAddress, 0));
        }
    }

    private static bool LooksLikeReolinkReply(byte[] payload)
        => payload.Length >= 4 && payload[0] == 0xAA && payload[1] == 0xAA && payload[2] == 0x00 && payload[3] == 0x00;

    private static DiscoveredDevice ParseResponse(byte[] payload, IPAddress sourceAddress)
    {
        var text = Encoding.ASCII.GetString(payload);
        var printable = new string(text.Select(character =>
            character is >= (char)32 and <= (char)126 ? character : ' ').ToArray());

        var ip = (FindIpv4(payload) ?? sourceAddress).ToString();
        var mac = FindMac(payload);
        var name = FindTextCandidate(printable, sourceAddress.ToString(), mac);
        var uid = FindUid(printable);

        return new DiscoveredDevice
        {
            IpAddress = ip,
            MacAddress = mac,
            Hostname = name,
            Manufacturer = "Reolink",
            Model = name,
            SerialNumber = uid,
            CameraEvidence = true,
            AssignedProviderName = "Reolink LAN Discovery",
            HttpSupported = true,
            HttpPort = 80,
            RtspSupported = true,
            RtspPort = 554,
            Status = DeviceStatus.Online
        };
    }

    private static string? FindUid(string text)
    {
        var markers = new[] { "UID=", "uid=", "UID:", "uid:" };
        foreach (var marker in markers)
        {
            var index = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (index < 0) continue;
            var start = index + marker.Length;
            var value = text[start..].Split(' ', '\0', '\r', '\n', '\t', ';', ',')[0].Trim();
            if (value.Length >= 4) return value;
        }
        return null;
    }

    private static string? FindTextCandidate(string text, string sourceIp, string? mac)
    {
        foreach (var candidate in text.Split(' ', '\0', '\r', '\n', '\t')
                     .Select(value => value.Trim(' ', ':', ';', ',', '"'))
                     .Where(value => value.Length is >= 3 and <= 64))
        {
            if (candidate.Equals(sourceIp, StringComparison.OrdinalIgnoreCase)) continue;
            if (candidate.Equals(mac, StringComparison.OrdinalIgnoreCase)) continue;
            if (candidate.Contains("aaaa0000", StringComparison.OrdinalIgnoreCase)) continue;
            if (IPAddress.TryParse(candidate, out _)) continue;
            if (candidate.All(character => char.IsLetterOrDigit(character) || character is '-' || character is '_' || character is '.'))
                return candidate;
        }
        return null;
    }

    private static string? FindMac(byte[] payload)
    {
        for (var index = 0; index <= payload.Length - 6; index++)
        {
            var bytes = payload.AsSpan(index, 6);
            var isMac = bytes.ToArray().Count(value => value is 0x00 or 0xFF) < 5;
            if (!isMac) continue;

            var value = string.Join(":", bytes.ToArray().Select(item => item.ToString("X2")));
            var text = value.Replace(":", string.Empty, StringComparison.Ordinal);
            if (text.Length == 12)
                return value;
        }
        return null;
    }

    private static IPAddress? FindIpv4(byte[] payload)
    {
        for (var index = 0; index <= payload.Length - 4; index++)
        {
            var first = payload[index];
            if (first is 0 or 127 or 224 or 255) continue;
            var address = new IPAddress(payload.AsSpan(index, 4));
            if (IPAddress.IsLoopback(address)) continue;
            var bytes = address.GetAddressBytes();
            if (bytes[0] >= 1 && bytes[0] <= 223)
                return address;
        }
        return null;
    }

    private static IPAddress CalculateBroadcastAddress(IPAddress ipAddress, IPAddress subnetMask)
    {
        var ip = ipAddress.GetAddressBytes();
        var mask = subnetMask.GetAddressBytes();
        if (ip.Length != 4 || mask.Length != 4)
            return IPAddress.Broadcast;

        var broadcast = new byte[4];
        for (var index = 0; index < 4; index++)
            broadcast[index] = (byte)(ip[index] | ~mask[index]);
        return new IPAddress(broadcast);
    }
}
