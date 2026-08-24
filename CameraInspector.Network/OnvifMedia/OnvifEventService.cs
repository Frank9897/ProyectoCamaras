using System.Globalization;
using System.Security;
using System.Xml.Linq;
using CameraInspector.Core.Interfaces;
using CameraInspector.Core.Models;

namespace CameraInspector.Network.OnvifMedia;

/// <summary>
/// Implementación de eventos ONVIF mediante PullMessages.
/// Evita abrir un listener HTTP entrante en el equipo del técnico.
/// </summary>
public sealed class OnvifEventService : IOnvifEventService
{
    private readonly IOnvifDeviceService _deviceService;

    public OnvifEventService(IOnvifDeviceService deviceService)
    {
        // _deviceService obtiene el XAddr real del Event Service.
        _deviceService = deviceService;
    }

    public async Task<IReadOnlyList<OnvifEventInfo>> PullMessagesAsync(
        DiscoveredDevice device,
        string? username,
        string? password,
        int timeoutSeconds = 5,
        int messageLimit = 20,
        CancellationToken cancellationToken = default)
    {
        // Los límites evitan crear peticiones de larga duración o respuestas excesivamente grandes.
        var safeTimeout = Math.Clamp(timeoutSeconds, 1, 30);
        var safeLimit = Math.Clamp(messageLimit, 1, 100);

        var capabilities = await _deviceService.GetCapabilitiesAsync(
            device,
            username,
            password,
            cancellationToken);

        var endpoint = capabilities?.EventsServiceXAddr;
        if (string.IsNullOrWhiteSpace(endpoint))
            return [];

        // PullMessages necesita un SubscriptionReference. Creamos una suscripción temporal
        // para consultar eventos sin mantener estado persistente en la aplicación.
        var reference = await CreatePullPointSubscriptionAsync(
            endpoint,
            username,
            password,
            cancellationToken);

        if (string.IsNullOrWhiteSpace(reference))
            return [];

        var timeout = $"PT{safeTimeout.ToString(CultureInfo.InvariantCulture)}S";
        var body = $"""
                   <tev:PullMessages xmlns:tev="http://www.onvif.org/ver10/events/wsdl">
                     <tev:Timeout>{timeout}</tev:Timeout>
                     <tev:MessageLimit>{safeLimit.ToString(CultureInfo.InvariantCulture)}</tev:MessageLimit>
                   </tev:PullMessages>
                   """;

        // PullPointReference es el endpoint que recibimos al crear la suscripción temporal.
        var document = await OnvifSoapClient.PostAsync(
            reference,
            body,
            BuildSecurity(username, password),
            cancellationToken);

        if (document is null)
            return [];

        return OnvifSoapClient.AllElements(document, "NotificationMessage")
            .Select(ParseEvent)
            .Where(item => item is not null)
            .Select(item => item!)
            .ToList();
    }

    private async Task<string?> CreatePullPointSubscriptionAsync(
        string eventEndpoint,
        string? username,
        string? password,
        CancellationToken cancellationToken)
    {
        const string body = """
            <tev:CreatePullPointSubscription xmlns:tev="http://www.onvif.org/ver10/events/wsdl"/>
            """;

        var document = await OnvifSoapClient.PostAsync(
            eventEndpoint,
            body,
            BuildSecurity(username, password),
            cancellationToken);

        // ConsumerReference / Address contiene la URL donde PullMessages debe ser enviado.
        return document?
            .Descendants()
            .FirstOrDefault(element => element.Name.LocalName == "Address")?
            .Value
            .Trim();
    }

    private static OnvifEventInfo? ParseEvent(XElement message)
    {
        var topic = message
            .Elements()
            .FirstOrDefault(element => element.Name.LocalName == "Topic")?
            .Value;

        var source = message
            .Descendants()
            .FirstOrDefault(element => element.Name.LocalName == "Source")?
            .Value;

        var data = message
            .Descendants()
            .FirstOrDefault(element => element.Name.LocalName == "SimpleItem")?
            .Attribute("Value")?
            .Value;

        var time = message
            .Descendants()
            .FirstOrDefault(element => element.Name.LocalName == "Message")?
            .Attribute("UtcTime")?
            .Value;

        DateTimeOffset? parsedTime = DateTimeOffset.TryParse(
            time,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var utcTime)
            ? utcTime
            : null;

        return new OnvifEventInfo
        {
            Time = parsedTime,
            Topic = string.IsNullOrWhiteSpace(topic) ? null : topic.Trim(),
            Source = string.IsNullOrWhiteSpace(source) ? null : source.Trim(),
            Data = string.IsNullOrWhiteSpace(data) ? null : data.Trim()
        };
    }

    private static string? BuildSecurity(string? username, string? password) =>
        username is not null && password is not null
            ? WsSecurityHeaderBuilder.Build(username, password)
            : null;
}
