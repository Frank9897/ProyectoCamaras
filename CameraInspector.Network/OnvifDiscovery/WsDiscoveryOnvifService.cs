using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Xml.Linq;
using CameraInspector.Core.Interfaces;
using CameraInspector.Core.Models;

namespace CameraInspector.Network.OnvifDiscovery;

/// <summary>
/// Implementación del descubrimiento ONVIF mediante WS-Discovery.
/// Envía un Probe multicast y transforma cada ProbeMatch en un resultado de dominio.
/// </summary>
public sealed class WsDiscoveryOnvifService : IOnvifDiscoveryService
{
    private static readonly IPAddress MulticastAddress = IPAddress.Parse("239.255.255.250");
    private const int DiscoveryPort = 3702;

    private readonly TimeSpan _discoveryTimeout = TimeSpan.FromSeconds(2);

    public async Task<IReadOnlyList<OnvifDiscoveryResult>> DiscoverAsync(
        CancellationToken cancellationToken = default)
    {
        var messageId = $"uuid:{Guid.NewGuid()}";

        // probe es el XML de WS-Discovery enviado al grupo multicast ONVIF.
        var probe = BuildProbe(messageId);
        var payload = Encoding.UTF8.GetBytes(probe);

        using var socket = new UdpClient(AddressFamily.InterNetwork);
        socket.EnableBroadcast = true;

        // Reutilizamos la interfaz local disponible para enviar el Probe al grupo ONVIF.
        var endpoint = new IPEndPoint(MulticastAddress, DiscoveryPort);
        await socket.SendAsync(payload, payload.Length, endpoint);

        var results = new List<OnvifDiscoveryResult>();
        var deadline = DateTimeOffset.UtcNow + _discoveryTimeout;

        while (!cancellationToken.IsCancellationRequested)
        {
            var remaining = deadline - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero)
                break;

            // receiveTask representa una espera única por una respuesta ProbeMatch.
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

            // Evitamos duplicar respuestas del mismo dispositivo/XAddr.
            if (results.Any(item => string.Equals(
                    item.DeviceServiceXAddr,
                    parsed.DeviceServiceXAddr,
                    StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

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

            // deviceReference identifica el recurso lógico anunciado por el dispositivo.
            var deviceReference = document.Descendants()
                .FirstOrDefault(element => element.Name.LocalName == "EndpointReference")?
                .Value;

            // xAddr es el endpoint real del Device Service publicado por WS-Discovery.
            var xAddr = document.Descendants()
                .FirstOrDefault(element => element.Name.LocalName == "XAddrs")?
                .Value?
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault(IsAbsoluteHttpUri);

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
                MessageId = deviceReference,
                DeviceServiceXAddr = xAddr,
                Types = string.IsNullOrWhiteSpace(types) ? null : types,
                Scopes = string.IsNullOrWhiteSpace(scopes) ? null : scopes
            };
        }
        catch (Exception)
        {
            // Un paquete multicast malformado no debe interrumpir el descubrimiento de los demás dispositivos.
            return null;
        }
    }

    /// <summary>
    /// Valida que una cadena represente una URL HTTP/HTTPS absoluta.
    /// </summary>
    private static bool IsAbsoluteHttpUri(string value)
    {
        // uri recibe la cadena candidata convertida a Uri.
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
            return false;

        // El endpoint debe utilizar HTTP o HTTPS para poder ser consumido por las capas ONVIF posteriores.
        return string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
               || string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
    }
}
