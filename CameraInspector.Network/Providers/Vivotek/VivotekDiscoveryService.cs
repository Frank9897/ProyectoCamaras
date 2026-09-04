using System.Net;
using System.Net.NetworkInformation;
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
    private readonly TimeSpan _discoveryTimeout = TimeSpan.FromSeconds(1.8);

    public async Task<IReadOnlyList<DiscoveredDevice>> DiscoverAsync(
        NetworkInterfaceInfo networkInterface,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(networkInterface);

        var bindAddresses = GetInterfaceIpv4Addresses(networkInterface);
        if (bindAddresses.Count == 0)
            bindAddresses = [networkInterface.IpAddress];

        var tasks = bindAddresses
            .Distinct()
            .Select(address => DiscoverFromAddressAsync(address, networkInterface, cancellationToken))
            .ToList();

        var batches = await Task.WhenAll(tasks);
        var results = new Dictionary<string, DiscoveredDevice>(StringComparer.OrdinalIgnoreCase);

        foreach (var batch in batches)
        {
            foreach (var device in batch)
            {
                var key = device.MacAddress ?? device.IpAddress;
                if (results.TryGetValue(key, out var existing))
                    MergeEvidence(existing, device);
                else
                    results[key] = device;
            }
        }

        return results.Values.ToList();
    }

    private async Task<IReadOnlyList<DiscoveredDevice>> DiscoverFromAddressAsync(
        IPAddress bindAddress,
        NetworkInterfaceInfo networkInterface,
        CancellationToken cancellationToken)
    {
        var results = new Dictionary<string, DiscoveredDevice>(StringComparer.OrdinalIgnoreCase);
        var broadcastAddress = CalculateBroadcastAddress(bindAddress, networkInterface.SubnetMask);
        var broadcasts = new List<IPAddress>
        {
            broadcastAddress,
            IPAddress.Broadcast,
            IPAddress.Parse("169.254.255.255")
        };

        using var socket = CreateBoundSocket(bindAddress, ShepherdDiscoveryPort, cancellationToken);
        socket.EnableBroadcast = true;

        var probes = BuildProbes();
        foreach (var target in broadcasts.Distinct())
        {
            cancellationToken.ThrowIfCancellationRequested();

            foreach (var targetPort in new[] { ShepherdDiscoveryPort, LegacyDiscoveryPort })
            {
                foreach (var probe in probes)
                {
                    try
                    {
                        await socket.SendAsync(probe, probe.Length, new IPEndPoint(target, targetPort));
                    }
                    catch (SocketException)
                    {
                    }
                }
            }
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

                // Nunca interpretar una respuesta generada por el propio equipo como una cámara.
                if (IPAddress.IsLoopback(packet.RemoteEndPoint.Address) ||
                    packet.RemoteEndPoint.Address.Equals(bindAddress))
                    continue;

                if (!LooksLikeVivotekResponse(packet.Buffer))
                    continue;

                var device = CreateDiscoveredDevice(packet.RemoteEndPoint.Address, packet.Buffer);
                if (device.MacAddress is null)
                    continue;

                var key = device.MacAddress;
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

        return results.Values.ToList();
    }

    private static List<IPAddress> GetInterfaceIpv4Addresses(NetworkInterfaceInfo networkInterface)
    {
        try
        {
            var nic = NetworkInterface.GetAllNetworkInterfaces()
                .FirstOrDefault(item => item.Id.Equals(networkInterface.InterfaceId, StringComparison.OrdinalIgnoreCase));

            if (nic is null)
                return [];

            return nic.GetIPProperties()
                .UnicastAddresses
                .Where(item => item.Address.AddressFamily == AddressFamily.InterNetwork)
                .Select(item => item.Address)
                .Distinct()
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    private static UdpClient CreateBoundSocket(
        IPAddress bindAddress,
        int preferredPort,
        CancellationToken cancellationToken)
    {
        try
        {
            var socket = new UdpClient(new IPEndPoint(bindAddress, preferredPort));
            socket.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Broadcast, true);
            return socket;
        }
        catch (SocketException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var socket = new UdpClient(new IPEndPoint(bindAddress, 0));
            socket.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Broadcast, true);
            return socket;
        }
    }

    private static IReadOnlyList<byte[]> BuildProbes()
    {
        var session = Guid.NewGuid().ToByteArray();

        return
        [
            new[] { (byte)0x01, session[0], session[1], session[2], (byte)0x03 },
            new[] { (byte)0x01, session[0], session[1], session[2], session[3], (byte)0x03 },
            Encoding.ASCII.GetBytes("VIVOTEK")
        ];
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
        // La cadena "VIVOTEK" por sí sola no identifica una cámara: puede aparecer
        // en tráfico ajeno, respuestas reflejadas o paquetes de prueba. Exigimos una
        // estructura que contenga la MAC VIVOTEK esperada antes de producir evidencia fuerte.
        return payload.Length >= 11 && ExtractVivotekMac(payload) is not null;
    }

    private static DiscoveredDevice CreateDiscoveredDevice(IPAddress sourceAddress, byte[] payload)
    {
        var advertisedIp = ExtractVivotekIp(payload) ?? sourceAddress;
        var mac = ExtractVivotekMac(payload);
        var model = ExtractModel(payload);

        var device = new DiscoveredDevice
        {
            IpAddress = advertisedIp.ToString(),
            MacAddress = mac,
            Manufacturer = "VIVOTEK",
            Model = model,
            AssignedProviderName = "VIVOTEK",
            Status = DeviceStatus.Online,
            CameraEvidence = mac is not null,
            HttpSupported = true,
            HttpPort = 80,
            RtspSupported = true,
            RtspPort = 554
        };

        device.AddEvidence(
            "VIVOTEK Discovery",
            0.99,
            $"respuesta propietaria VIVOTEK con identidad MAC {mac}",
            mac is not null);

        return device;
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
        target.CameraEvidence |= source.CameraEvidence;
        target.HttpSupported |= source.HttpSupported;
        target.RtspSupported |= source.RtspSupported;
        target.HttpPort ??= source.HttpPort;
        target.RtspPort ??= source.RtspPort;
        target.Status = DeviceStatus.Online;

        foreach (var evidence in source.DetectionEvidence)
            target.AddEvidence(evidence.Method, evidence.Confidence, evidence.Details, evidence.IsCameraEvidence);
    }
}
