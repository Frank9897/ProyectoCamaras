using CameraInspector.Core.Models;

namespace CameraInspector.Core.Interfaces;

/// <summary>
/// Contrato común para servicios de configuración de red "legacy" (no-ONVIF) por
/// fabricante: VIVOTEK (CGI), DAHUA (configManager CGI), HIKVISION (ISAPI XML).
/// Permite que la UI elija el servicio correcto sin duplicar la lógica de
/// orquestación (reintentos, mensajes, fallback a ONVIF) por cada fabricante.
/// </summary>
public interface ILegacyCameraNetworkConfigurationService
{
    Task<OnvifNetworkConfiguration?> GetNetworkConfigurationAsync(
        DiscoveredDevice device,
        string username,
        string password,
        CancellationToken cancellationToken = default);

    Task<OnvifNetworkChangeResult> SetNetworkAsync(
        DiscoveredDevice device,
        string username,
        string password,
        bool useDhcp,
        string? ipv4Address,
        int? prefixLength,
        string? gatewayAddress,
        CancellationToken cancellationToken = default);
}
