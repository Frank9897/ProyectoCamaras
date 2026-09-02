using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Xml.Linq;
using CameraInspector.Core.Interfaces;
using CameraInspector.Core.Models;

namespace CameraInspector.Network.OnvifDiscovery;

/// <summary>
/// Implementación del descubrimiento ONVIF mediante WS-Discovery.
/// El socket puede quedar asociado a una interfaz concreta para evitar que Windows
/// envíe el multicast por otro adaptador distinto al puerto que el técnico seleccionó.
/// </summary>
public sealed class WsDiscoveryOnvifService : IOnvifDiscoveryService
{
    private static readonly IPAddress MulticastAddress = IPAddress.Parse("239.255.255.250");
    private const int DiscoveryPort = 3702;

    // En una LAN local las cámaras suelen responder inmediatamente. Una ventana corta
    // evita que una cámara ausente haga lenta toda la detección.
    private readonly TimeSpan _discoveryTimeout = TimeSpan.FromSeconds(1.2);

    public async Task<IReadOnlyList<OnvifDiscoveryResult>> DiscoverAsync(
        NetworkInterfaceInfo? networkInterface = null,
        CancellationToken cancellationToken = default)
    {
        var messageId = $"uuid:{Guid.NewGuid()}";
        var probe = BuildProbe(messageId);
        var payload = Encoding.UTF8.GetBytes(probe);

        using var socket = networkInterface is null
            ? new UdpClient(AddressFamily.InterNetwork)
            : new UdpClient(new IPEndPoint(networkInterface.IpAddress, 0));

        socket.EnableBroadcast = true;

        if (networkInterface is not null)
            socket.JoinMulticastGroup(MulticastAddress, networkInterface.IpAddress);

        var endpoint = new IPEndPoint(MulticastAddress, DiscoveryPort);
        await socket.SendAsync(payload, payload.Length, endpoint);

        var results = new List<OnvifDiscoveryResult>();
        var deadline = DateTimeOffset.UtcNow + _discoveryTimeout;

        while (!cancellationToken.IsCancellationRequested)
        {
            var remaining = deadline - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero)
                break;

            var receiveTask = socket.ReceiveAsync(cancellationToken).AsTask();
            var completedTask = await Task.WhenAny(
                receiveTask,
                Task.Delay(remaining, cancellationToken));

            if (completedTask != receiveTask)
                break;

            var packet = await receiveTask;
            var parsed = ParseProbeMatch(packet.Buffer);
            if (parsed is null)
                continue;

            if (results.Any(item => string.Equals(
                    item.DeviceServiceXAddr,
                    parsed.DeviceServiceXAddr,
                    StringComparison.OrdinalIgnoreCase)))
                continue;

            results.Add(parsed);
        }

        return results;
    }

    private static string BuildProbe(string messageId) => $"""
        <e:Envelope xmlns:e="http://www.w3.org/2003/05/soap-envelope"
                    xmlns:a="http://www.w3.org/2005/08/addressing"
                    xmlns:d="http://docs.oasis-open.org/ws-dd/ns/discovery/2009/01"
                    xmlns:dn="http://www.onvif.org/ver10/network/wsdl">
          <e:Header>
            <a:Action>http://docs.oasis-open.org/ws-dd/ns/discovery/2009/01/Probe</a:Action>
            <a:MessageID>{messageId}</a:MessageID>
            <a:To>urn:schemas-xmlsoap-org:ws:2005:04:discovery</a:To>
          </e:Header>
          <e:Body>
            <d:Probe>
              <d:Types>dn:NetworkVideoTransmitter</d:Types>
            </d:Probe>
          </e:Body>
        </e:Envelope>
        """;

    private static OnvifDiscoveryResult? ParseProbeMatch(byte[] payload)
    {
        try
        {
            var document = XDocument.Parse(Encoding.UTF8.GetString(payload));

            var deviceReference = document.Descendants()
                .FirstOrDefault(element => element.Name.LocalName == "EndpointReference")?
                .Value;

            var xAddrs = document.Descendants()
                .FirstOrDefault(element => element.Name.LocalName == "XAddrs")?
                .Value?
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

            if (xAddrs is null)
                return null;

            var xAddr = xAddrs.FirstOrDefault(IsAbsoluteHttpUri);
            if (string.IsNullOrWhiteSpace(xAddr))
                return null;

            var types = document.Descendants()
                .FirstOrDefault(element => element.Name.LocalName == "Types")?
                .Value;

            var scopes = document.Descendants()
                .FirstOrDefault(element => element.Name.LocalName == "Scopes")?
                .Value;

            return new OnvifDiscoveryResult
            {
                MessageId = deviceReference ?? string.Empty,
                DeviceServiceXAddr = xAddr,
                Types = string.IsNullOrWhiteSpace(types) ? null : types,
                Scopes = string.IsNullOrWhiteSpace(scopes) ? null : scopes
            };
        }
        catch
        {
            return null;
        }
    }

    private static bool IsAbsoluteHttpUri(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
            return false;

        return string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
               || string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
    }
}