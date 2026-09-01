using System.Net;
using System.Net.Sockets;
using System.Text;
using CameraInspector.Core.Interfaces;
using CameraInspector.Core.Models;

namespace CameraInspector.Network.Providers.Vivotek;

/// <summary>
/// Descubrimiento propietario de VIVOTEK compatible con Shepherd/IW2.
/// Soporta LAN normal y cámaras con Alias IP/APIPA 169.254.x.x.
/// </summary>
public sealed class VivotekDiscoveryService : IVivotekDiscoveryService
{
    private const int ShepherdDiscoveryPort = 5678;
    private const int LegacyDiscoveryPort = 10000;
    private readonly TimeSpan _discoveryTimeout = TimeSpan.FromSeconds(3);

    public async Task<IReadOnlyList<DiscoveredDevice>> DiscoverAsync(
        NetworkInterfaceInfo networkInterface,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(networkInterface);

        var results = new Dictionary<string, DiscoveredDevice>(StringComparer.OrdinalIgnoreCase);
        var bindAddress = networkInterface.IpAddress;
        var broadcastAddress = CalculateBroadcastAddress(bindAddress, networkInterface.SubnetMask);
        var broadcasts = new List<IPAddress> { broadcastAddress, IPAddress.Broadcast };

        if (IsApipa(bindAddress))
            broadcasts.Add(IPAddress.Parse("169.254.255.255"));

        foreach (var target in broadcasts.Distinct())
        {
            cancellationToken.ThrowIfCancellationRequested();
            await ProbePortAsync(bindAddress, target, ShepherdDiscoveryPort, results, cancellationToken);
            await ProbePortAsync(bindAddress, target, LegacyDiscoveryPort, results, cancellationToken);
        }

        return results.Values.ToList();
    }

    private async Task ProbePortAsync(
        IPAddress bindAddress,
        IPAddress target,
        int targetPort,
        Dictionary<string, DiscoveredDevice> results,
        CancellationToken cancellationToken)
    {
        using var socket = await CreateBoundSocketAsync(bindAddress, ShepherdDiscoveryPort, cancellationToken);
        socket.EnableBroadcast = true;
        var probe = BuildProbe();

        try
        {
            await socket.SendAsync(probe, probe.Length, new IPEndPoint(target, targetPort));
        }
        catch (SocketException)
        {
            return;
        }

        var deadline = DateTimeOffset.UtcNow + _discoveryTimeout;
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
                if (!LooksLikeVivotekResponse(packet.Buffer))
                    continue;

                var device = CreateDiscoveredDevice(packet.RemoteEndPoint.Address, packet.Buffer);
                var key = device.MacAddress ?? device.IpAddress;

                if (results.TryGetValue(key, out var existing))
                    MergeEvidence(existing, device);
                else
                    results[key] = device;
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
    }

    private static async Task<UdpClient> CreateBoundSocketAsync(
        IPAddress bindAddress,
        int preferredPort,
        CancellationToken cancellationToken)
    {
        try
        {
            return new UdpClient(new IPEndPoint(bindAddress, preferredPort));
        }
        catch (SocketException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new UdpClient(new IPEndPoint(bindAddress, 0));
        }
    }

    private static byte[] BuildProbe()
    {
        var session = Guid.NewGuid().ToByteArray();
        return new[] { (byte)0x01, session[0], session[1], session[2], (byte)0x03 };
    }

    private static IPAddress CalculateBroadcastAddress(IPAddress ipAddress, IPAddress subnetMask)
    {
        var ipBytes = ipAddress.GetAddressBytes();
        var maskBytes = subnetMask.GetAddressBytes();
        if (ipBytes.Length != 4 || maskBytes.Length != 4)
            return IPAddress.Broadcast;

        var broadcast = new byte[4];
        for (var index = 0; index < 4; index++)
            broadcast[index] = (byte)(ipBytes[index] | ~maskBytes[index]);
        return new IPAddress(broadcast);
    }

    private static bool IsApipa(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return bytes.Length == 4 && bytes[0] == 169 && bytes[1] == 254;
    }

    private static bool LooksLikeVivotekResponse(byte[] payload)
    {
        if (payload.Length < 11 || payload[0] != 0x02)
            return false;

        // La respuesta propietaria observada de VIVOTEK contiene TLV y la firma MAC 00-02-D1.
        return ExtractVivotekMac(payload) is not null ||
               Encoding.ASCII.GetString(payload).Contains("VIVOTEK", StringComparison.OrdinalIgnoreCase);
    }

    private static DiscoveredDevice CreateDiscoveredDevice(IPAddress sourceAddress, byte[] payload)
    {
        var advertisedIp = ExtractVivotekIp(payload) ?? sourceAddress;
        var mac = ExtractVivotekMac(payload);
        var model = ExtractModel(payload);

        return new DiscoveredDevice
        {
            IpAddress = advertisedIp.ToString(),
            MacAddress = mac,
            Manufacturer = "VIVOTEK",
            Model = model,
            AssignedProviderName = "VIVOTEK",
            Status = DeviceStatus.Online,
            CameraEvidence = true,
            HttpSupported = true,
            HttpPort = 80,
            RtspSupported = true,
            RtspPort = 554
        };
    }

    private static string? ExtractVivotekMac(byte[] payload)
    {
        for (var index = 0; index <= payload.Length - 6; index++)
        {
            if (payload[index] != 0x00 || payload[index + 1] != 0x02 || payload[index + 2] != 0xD1)
                continue;

            return string.Join(":", payload.Skip(index).Take(6).Select(value => value.ToString("X2")));
        }
        return null;
    }

    private static IPAddress? ExtractVivotekIp(byte[] payload)
    {
        // Formato TLV observado: tag 0x03 + length 0x04 + IPv4 en cuatro octetos.
        for (var index = 0; index <= payload.Length - 6; index++)
        {
            if (payload[index] != 0x03 || payload[index + 1] != 0x04)
                continue;

            var bytes = payload.Skip(index + 2).Take(4).ToArray();
            if (IPAddress.TryParse(string.Join('.', bytes), out var address))
                return address;
        }
        return null;
    }

    private static string? ExtractModel(byte[] payload)
    {
        var candidates = new List<string>();
        var current = new StringBuilder();
        foreach (var value in payload)
        {
            if (value is >= 0x20 and <= 0x7E)
            {
                current.Append((char)value);
                continue;
            }

            if (current.Length >= 5)
                candidates.Add(current.ToString());
            current.Clear();
        }
        if (current.Length >= 5)
            candidates.Add(current.ToString());

        return candidates
            .Where(item => item.Length <= 48)
            .FirstOrDefault(item => item.Any(char.IsLetter) && item.Any(char.IsDigit));
    }

    private static void MergeEvidence(DiscoveredDevice target, DiscoveredDevice source)
    {
        target.MacAddress ??= source.MacAddress;
        target.Model ??= source.Model;
        target.FirmwareVersion ??= source.FirmwareVersion;
        target.SerialNumber ??= source.SerialNumber;
        target.Manufacturer = "VIVOTEK";
        target.AssignedProviderName = "VIVOTEK";
        target.CameraEvidence = true;
        target.HttpSupported |= source.HttpSupported;
        target.RtspSupported |= source.RtspSupported;
        target.HttpPort ??= source.HttpPort;
        target.RtspPort ??= source.RtspPort;
        target.Status = DeviceStatus.Online;
    }
}
