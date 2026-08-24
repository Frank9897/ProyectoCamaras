using System.Net.Http.Headers;
using System.Text;
using System.Xml.Linq;

namespace CameraInspector.Network.OnvifMedia;

/// <summary>
/// Helper mínimo para POSTear un body SOAP a un servicio ONVIF y parsear la respuesta.
/// Se mantiene crudo (sin librería ONVIF completa) por la misma razón que OnvifProbeDetector:
/// evitar atar el MVP a una dependencia grande antes de tener el resto de los providers.
/// </summary>
internal static class OnvifSoapClient
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMilliseconds(2000) };

    /// <param name="securityHeaderXml">Header WS-Security ya armado (ver WsSecurityHeaderBuilder), o null si el servicio no requiere auth.</param>
    public static async Task<XDocument?> PostAsync(
        string endpointUrl,
        string bodyXml,
        string? securityHeaderXml,
        CancellationToken cancellationToken)
    {
        var envelope = $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <soap:Envelope xmlns:soap="http://www.w3.org/2003/05/soap-envelope">
              <soap:Header>
                {securityHeaderXml ?? ""}
              </soap:Header>
              <soap:Body>
                {bodyXml}
              </soap:Body>
            </soap:Envelope>
            """;

        using var content = new StringContent(envelope, Encoding.UTF8);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/soap+xml");

        using var response = await Http.PostAsync(endpointUrl, content, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return null;

        var xml = await response.Content.ReadAsStringAsync(cancellationToken);
        return XDocument.Parse(xml);
    }

    /// <summary>Busca el primer elemento con ese nombre local, sin importar el namespace/prefijo que use el firmware.</summary>
    public static string? FirstValue(XDocument doc, string localName) =>
        doc.Descendants().FirstOrDefault(el => el.Name.LocalName == localName)?.Value;

    public static IEnumerable<XElement> AllElements(XDocument doc, string localName) =>
        doc.Descendants().Where(el => el.Name.LocalName == localName);
}
