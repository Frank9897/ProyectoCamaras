using System.Net;

namespace CameraInspector.Core.Models;

/// <summary>
/// Representa una interfaz de red IPv4 real del equipo que puede utilizarse
/// como punto de entrada para descubrimiento y barrido de cámaras.
/// </summary>
public sealed class NetworkInterfaceInfo
{
    /// <summary>Nombre visible del adaptador en Windows.</summary>
    public required string Name { get; init; }

    /// <summary>Descripción proporcionada por el controlador de red.</summary>
    public required string Description { get; init; }

    /// <summary>Identificador estable de la interfaz entregado por Windows.</summary>
    public required string InterfaceId { get; init; }

    /// <summary>Dirección física MAC del adaptador, cuando Windows la proporciona.</summary>
    public string? MacAddress { get; init; }

    /// <summary>Dirección IPv4 configurada en esta interfaz.</summary>
    public required IPAddress IpAddress { get; init; }

    /// <summary>Máscara IPv4 configurada en esta interfaz.</summary>
    public required IPAddress SubnetMask { get; init; }

    /// <summary>Prefijo CIDR calculado desde la máscara, por ejemplo 24.</summary>
    public int CidrPrefixLength { get; init; }

    /// <summary>Dirección de red calculada automáticamente a partir de IP y máscara.</summary>
    public required IPAddress NetworkAddress { get; init; }

    /// <summary>Gateway IPv4 preferido informado por Windows.</summary>
    public IPAddress? DefaultGateway { get; init; }

    /// <summary>Servidores DNS IPv4 asociados a la interfaz.</summary>
    public IReadOnlyList<IPAddress> DnsServers { get; init; } = Array.Empty<IPAddress>();

    /// <summary>Texto listo para mostrar en el diagnóstico local.</summary>
    public string DnsServersDisplay => DnsServers.Count == 0
        ? "Sin DNS configurado"
        : string.Join(", ", DnsServers);

    /// <summary>Indica si Windows informa que la interfaz obtiene su configuración mediante DHCP.</summary>
    public bool UsesDhcp { get; init; }

    /// <summary>Indica si el adaptador está operativo.</summary>
    public bool IsUp { get; init; }

    /// <summary>Indica si Windows clasifica el adaptador como Wi-Fi.</summary>
    public bool IsWireless { get; init; }

    /// <summary>
    /// Texto compacto para el selector. Primero se muestra el nombre del puerto
    /// y después su red calculada, sin convertir la IP en el título principal.
    /// </summary>
    public override string ToString()
    {
        var gatewayText = DefaultGateway is null ? "sin gateway" : $"GW {DefaultGateway}";
        var dhcpText = UsesDhcp ? "DHCP" : "FIJA";
        return $"{Name} · {IpAddress}/{CidrPrefixLength} · {dhcpText} · {gatewayText}";
    }
}
