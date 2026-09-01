using System.Net;
using System.Net.Sockets;
using System.Text;
using CameraInspector.Core.Models;

namespace CameraInspector.Network;

/// <summary>
/// Descubrimiento UPnP/SSDP sobre la interfaz seleccionada.
/// Resulta útil para cámaras antiguas que no implementan ONVIF.
/// </summary>
public sealed class SsdpDiscoveryService
{
    private static readonly IPAddress MulticastAddress = IPAddress.Parse("239.255.255.250");
    private const int DiscoveryPort = 1900;
    private readonly TimeSpan _timeout = TimeSpan.FromSeconds(2.5);

    public async Task<IReadOnlyList<DiscoveredDevice>> DiscoverAsync(
        NetworkInterfaceInfo networkInterface,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(networkInterface);

        using var socket = new UdpClient(new IPEndPoint(networkInterface.IpAddress, 0));
        socket.EnableBroadcast = true;

        var probe = Encoding.ASCII.GetBytes(
            "M-SEARCH * HTTP/1.1\r\n" +
            "HOST: 239.255.255.250:1900\r\n" +
            "MAN: \"ssdp:discover\"\r\n" +
            "MX: 2\r\n" +
            "ST: ssdp:all\r\n\r\n");

        await socket.SendAsync(probe, probe.Length,
            new IPEndPoint(MulticastAddress, DiscoveryPort));

        var results = new Dictionary<string, DiscoveredDevice>(StringComparer.OrdinalIgnoreCase);
        var deadline = DateTimeOffset.UtcNow + _timeout;

        while (!cancellationToken.IsCancellationRequested)
        {
            var remaining = deadline - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero)
                break;

            try
            {
                var receiveTask = socket.ReceiveAsync(cancellationToken).AsTask();
                var completed = await Task.WhenAny(
                    receiveTask,
                    Task.Delay(remaining, cancellationToken));

                if (completed != receiveTask)
                    break;

                var packet = await receiveTask;
                var headers = ParseHeaders(packet.Buffer);
                if (!headers.TryGetValue("location", out var location))
                    continue;

                if (!Uri.TryCreate(location, UriKind.Absolute, out var uri) ||
                    uri.Host.Length == 0 ||
                    !IPAddress.TryParse(uri.Host, out var ip))
                    continue;

                headers.TryGetValue("server", out var server);
                headers.TryGetValue("usn", out var usn);

                var isVivotek = ContainsAny(
                    server,
                    headers.GetValueOrDefault("st"),
                    headers.GetValueOrDefault("ext"),
                    usn,
                    location,
                    "vivotek");

                if (!isVivotek && !LooksLikeCameraDevice(server, usn, location))
                    continue;

                if (!results.TryGetValue(ip.ToString(), out var device))
                {
                    device = new DiscoveredDevice
                    {
                        IpAddress = ip.ToString(),
                        Status = DeviceStatus.Online,
                        HttpSupported = uri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase),
                        HttpsSupported = uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase),
                        HttpPort = uri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase)
                            ? (uri.IsDefaultPort ? 80 : uri.Port)
                            : null,
                        Manufacturer = isVivotek ? "VIVOTEK" : null,
                        AssignedProviderName = isVivotek ? "VIVOTEK" : null
                    };
                    results[ip.ToString()] = device;
                }
                else if (isVivotek)
                {
                    device.Manufacturer = "VIVOTEK";
                    device.AssignedProviderName = "VIVOTEK";
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

        return results.Values.ToList();
    }

    private static Dictionary<string, string> ParseHeaders(byte[] payload)
    {
        var text = Encoding.ASCII.GetString(payload);
        var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in lines.Skip(1))
        {
            var separator = line.IndexOf(':');
            if (separator <= 0)
                continue;

            var name = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();
            if (value.Length > 0)
                headers[name] = value;
        }

        return headers;
    }

    private static bool ContainsAny(params string?[] values)
        => values.Any(value => !string.IsNullOrWhiteSpace(value) &&
            value.Contains("vivotek", StringComparison.OrdinalIgnoreCase));

    private static bool LooksLikeCameraDevice(params string?[] values)
    {
        string[] keywords = { "camera", "network camera", "ipcam", "ip camera", "video server", "vvt", "vivotek" };
        return values.Any(value => !string.IsNullOrWhiteSpace(value) &&
            keywords.Any(keyword => value.Contains(keyword, StringComparison.OrdinalIgnoreCase)));
    }
}
