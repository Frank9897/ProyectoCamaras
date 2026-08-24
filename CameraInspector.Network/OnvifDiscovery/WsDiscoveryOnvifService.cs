using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Xml.Linq;
using CameraInspector.Core.Interfaces;
using CameraInspector.Core.Models;

namespace CameraInspector.Network.OnvifDiscovery;

/// <summary>
/// Implementación de WS-Discovery para localizar dispositivos ONVIF en la red local.
/// Utiliza UDP multicast sobre 239.255.255.250:3702 y procesa las respuestas ProbeMatch.
/// </summary>
public sealed class WsDiscoveryOnvifService : IOnvifDiscoveryService
{
    // Dirección multicast estándar utilizada por WS-Discovery para los mensajes Probe.
    private static readonly IPAddress MulticastAddress = IPAddress.Parse("239.255.255.250");

    // Puerto UDP estándar de WS-Discovery utilizado por dispositivos ONVIF.
    private const int DiscoveryPort = 3702;

    // Tiempo máximo que esperamos respuestas después de enviar el Probe.
    private static readonly TimeSpan ReceiveTimeout = TimeSpan.FromSeconds(2);

    // Tipos solicitados en el Probe. Incluimos Device y NetworkVideoTransmitter para
    // cubrir cámaras y equipos ONVIF que anuncian el tipo de forma diferente.
    private const string ProbeTypes =
        "tds:Device dn:NetworkVideoTransmitter";

    /// <inheritdoc />
    public async Task<IReadOnlyList<OnvifDiscoveryResult>> DiscoverAsync(
        CancellationToken cancellationToken = default)
    {
        // results acumula respuestas únicas. La clave será el DeviceServiceXAddr porque
        // el mismo dispositivo puede responder varias veces al Probe.
        var results = new Dictionary<string, OnvifDiscoveryResult>(StringComparer.OrdinalIgnoreCase);

        // client representa el socket UDP utilizado para enviar y recibir los mensajes.
        // UdpClient es suficiente porque WS-Discovery trabaja sobre datagramas UDP.
        using var client = new UdpClient(AddressFamily.InterNetwork);

        // endpoint define el destino multicast al que enviamos el mensaje Probe.
        var endpoint = new IPEndPoint(MulticastAddress, DiscoveryPort);

        // probeXml contiene el SOAP 1.2 utilizado por WS-Discovery.
        // Guid.NewGuid genera un MessageID distinto para cada escaneo.
        var messageId = $"urn:uuid:{Guid.NewGuid()}";
        var probeXml = BuildProbeMessage(messageId);
        var payload = Encoding.UTF8.GetBytes(probeXml);

        // ConfigureAwait(false) evita depender del contexto de UI durante la operación de red.
        await client.SendAsync(payload, payload.Length, endpoint)
            .ConfigureAwait(false);

        // deadline representa el instante exacto en el que debemos dejar de esperar respuestas.
        // Se utiliza junto con el CancellationToken para que el escaneo sea determinista.
        var deadline = DateTimeOffset.UtcNow + ReceiveTimeout;

        while (!cancellationToken.IsCancellationRequested)
        {
            // remaining calcula cuánto tiempo queda antes de cerrar la escucha.
            var remaining = deadline - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero)
                break;

            // receiveTask espera un único datagrama UDP.
            var receiveTask = client.ReceiveAsync(cancellationToken).AsTask();

            // delayTask es una espera independiente que nos permite salir cuando termina el timeout.
            var delayTask = Task.Delay(remaining, cancellationToken);

            // completed determina qué ocurre primero: llega una respuesta o vence el timeout.
            var completed = await Task.WhenAny(receiveTask, delayTask).ConfigureAwait(false);

            if (completed != receiveTask)
                break;

            // packet contiene el datagrama recibido y la IP/puerto de origen.
            var packet = await receiveTask.ConfigureAwait(false);

            try
            {
                // xml convierte el datagrama UTF-8 en texto XML.
                var xml = Encoding.UTF8.GetString(packet.Buffer);

                // document permite navegar la respuesta SOAP sin depender de los prefijos XML.
                var document = XDocument.Parse(xml);
                var result = ParseProbeMatch(document, messageId);

                // Una respuesta inválida se ignora para no detener el descubrimiento de otros dispositivos.
                if (result is null || string.IsNullOrWhiteSpace(result.DeviceServiceXAddr))
                    continue;

                // TryAdd evita que una segunda respuesta del mismo dispositivo duplique la lista final.
                results[result.DeviceServiceXAddr] = result;
            }
            catch (Exception) when (cancellationToken.IsCancellationRequested is false)
            {
                // Un paquete malformado o inesperado no debe abortar el escaneo completo.
                // Simplemente continuamos esperando posibles respuestas válidas.
            }
        }

        // ToList crea una instantánea independiente de la colección interna antes de devolverla.
        return results.Values.ToList();
    }

    /// <summary>
    /// Construye el Probe WS-Discovery.
    /// </summary>
    private static string BuildProbeMessage(string messageId)
    {
        // messageId identifica de forma única esta operación de descubrimiento.
        // Se reutiliza en el atributo RelatesTo al analizar las respuestas.
        return $"""
            <?xml version="1.0" encoding="utf-8"?>
            <s:Envelope xmlns:s="http://www.w3.org/2003/05/soap-envelope"
                        xmlns:a="http://www.w3.org/2005/08/addressing"
                        xmlns:d="http://docs.oasis-open.org/ws-dd/ns/discovery/1.1"
                        xmlns:dn="http://www.onvif.org/ver10/network/wsdl"
                        xmlns:tds="http://www.onvif.org/ver10/device/wsdl">
              <s:Header>
                <a:Action s:mustUnderstand="1">http://docs.oasis-open.org/ws-dd/ns/discovery/1.1/Probe</a:Action>
                <a:MessageID>{messageId}</a:MessageID>
                <a:To s:mustUnderstand="1">urn:docs-oasis-open-org:ws-dd:ns:discovery:2009:01</a:To>
              </s:Header>
              <s:Body>
                <d:Probe>
                  <d:Types>{ProbeTypes}</d:Types>
                </d:Probe>
              </s:Body>
            </s:Envelope>
            """;
    }

    /// <summary>
    /// Convierte un ProbeMatch SOAP en nuestro modelo interno de descubrimiento ONVIF.
    /// </summary>
    private static OnvifDiscoveryResult? ParseProbeMatch(
        XDocument document,
        string requestMessageId)
    {
        // probeMatch representa la respuesta concreta del dispositivo.
        var probeMatch = document
            .Descendants()
            .FirstOrDefault(element => element.Name.LocalName == "ProbeMatch");

        if (probeMatch is null)
            return null;

        // xAddrElement contiene una o varias URLs de Device Service anunciadas por ONVIF.
        var xAddrElement = probeMatch
            .Elements()
            .FirstOrDefault(element => element.Name.LocalName == "XAddrs");

        // deviceServiceXAddr tomará la primera URL que parezca válida.
        var deviceServiceXAddr = xAddrElement?
            .Value
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(IsAbsoluteHttpUri);

        if (string.IsNullOrWhiteSpace(deviceServiceXAddr))
            return null;

        // messageId conserva el identificador de la solicitud original para permitir diagnóstico.
        // Preferimos el RelatesTo de la respuesta; si el firmware no lo incluye, usamos el requestId.
        var relatesTo = document
            .Descendants()
            .FirstOrDefault(element => element.Name.LocalName == "RelatesTo")
            ?.Value
            ?.Trim();

        var resolvedMessageId = string.IsNullOrWhiteSpace(relatesTo)
            ? requestMessageId
            : relatesTo;

        // types contiene los tipos WS-Discovery anunciados por el dispositivo.
        var types = probeMatch
            .Elements()
            .FirstOrDefault(element => element.Name.LocalName == "Types")
            ?.Value
            ?.Trim();

        // scopes contiene etiquetas adicionales del fabricante, hardware o ubicación.
        var scopes = probeMatch
            .Elements()
            .FirstOrDefault(element => element.Name.LocalName == "Scopes")
            ?.Value
            ?.Trim();

        return new OnvifDiscoveryResult
        {
            MessageId = resolvedMessageId,
            DeviceServiceXAddr = deviceServiceXAddr,
            Types = string.IsNullOrWhiteSpace(types) ? null : types,
            Scopes = string.IsNullOrWhiteSpace(scopes) ? null : scopes
        };
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
        return uri.Scheme is Uri.UriSchemeHttp or Uri.UriSchemeHttps;
    }
}
