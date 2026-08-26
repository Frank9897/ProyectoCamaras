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

    private readonly TimeSpan _discoveryTimeout = TimeSpan.FromSeconds(2);

    public async Task<IReadOnlyList<OnvifDiscoveryResult>> DiscoverAsync(
        NetworkInterfaceInfo? networkInterface = null,
        CancellationToken cancellationToken = default)
    {
        // messageId identifica de forma única este Probe para depuración y correlación de respuestas.
        var messageId = $"uuid:{Guid.NewGuid()}";
        // probe contiene el mensaje WS-Discovery dirigido a NetworkVideoTransmitter.
        var probe = BuildProbe(messageId);
        // payload es el mensaje convertido a UTF-8 para enviarlo por UDP.
        var payload = Encoding.UTF8.GetBytes(probe);

        // Si conocemos la IP del puerto seleccionado, enlazamos el socket explícitamente a esa interfaz.
        using var socket = networkInterface is null
            ? new UdpClient(AddressFamily.InterNetwork)
            : new UdpClient(new IPEndPoint(networkInterface.IpAddress, 0));

        socket.EnableBroadcast = true;

        if (networkInterface is not null)
        {
            // JoinMulticastGroup fija la interfaz local que recibirá las respuestas multicast.
            socket.JoinMulticastGroup(MulticastAddress, networkInterface.IpAddress);
        }

        // endpoint representa el destino estándar de WS-Discovery.
        var endpoint = new IPEndPoint(MulticastAddress, DiscoveryPort);
        await socket.SendAsync(payload, payload.Length, endpoint);

        var results = new List<OnvifDiscoveryResult>();
        var deadline = DateTimeOffset.UtcNow + _discoveryTimeout;

        while (!cancellationToken.IsCancellationRequested)
        {
            // remaining evita esperar indefinidamente si la red no devuelve ProbeMatch.
            var remaining = deadline - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero)
                break;

            // receiveTask espera respuestas en el mismo socket asociado a la interfaz seleccionada.
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

            // Evitamos agregar dos veces el mismo Device Service anunciado por la cámara.
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

            // xAddrs contiene todos los endpoints publicados por WS-Discovery.
            var xAddrs = document.Descendants()
                .FirstOrDefault(element => element.Name.LocalName == "XAddrs")?
                .Value?
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

            if (xAddrs is null)
                return null;

            // xAddr es el primer endpoint HTTP/HTTPS absoluto que podremos consumir posteriormente.
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
                // MessageId representa la referencia publicada por el dispositivo; si el paquete
                // no la contiene conservamos una cadena vacía en lugar de propagar null al modelo.
                MessageId = deviceReference ?? string.Empty,
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

    private static bool IsAbsoluteHttpUri(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
            return false;

        return string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
               || string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
    }
}
