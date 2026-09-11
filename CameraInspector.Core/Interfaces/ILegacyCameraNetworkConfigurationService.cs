using CameraInspector.Core.Models;

namespace CameraInspector.Core.Interfaces;

/// <summary>
/// Contrato común para servicios de configuración/administración "legacy" (no-ONVIF)
/// por fabricante: VIVOTEK (CGI), DAHUA (configManager CGI), HIKVISION (ISAPI XML).
/// Permite que la UI (NetworkConfigurationEditViewModel, CameraAccessSetupWindow) trate
/// a los tres fabricantes de forma uniforme, sin "if VIVOTEK / else ONVIF" hardcodeado,
/// mientras cada implementación resuelve la parte específica de su protocolo.
/// </summary>
public interface ILegacyCameraNetworkConfigurationService
{
    /// <summary>Usuario administrativo por defecto de fábrica para este fabricante
    /// (ej. "root" en VIVOTEK, "admin" en DAHUA/HIKVISION), usado quando aún no hay
    /// credenciales guardadas para la cámara.</summary>
    string DefaultAdminUsername { get; }

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

    Task<OnvifNetworkChangeResult> SetHostnameAsync(
        DiscoveredDevice device,
        string username,
        string password,
        string hostname,
        CancellationToken cancellationToken = default);

    Task<OnvifNetworkChangeResult> RebootAsync(
        DiscoveredDevice device,
        string username,
        string password,
        CancellationToken cancellationToken = default);

    Task<OnvifNetworkChangeResult> FactoryResetAsync(
        DiscoveredDevice device,
        string username,
        string password,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Configura/cambia la contraseña administrativa. "currentPassword" vacío significa
    /// "probar primero con el acceso de fábrica sin contraseña" (patrón usado por
    /// CameraAccessSetupWindow para cámaras recién detectadas sin credenciales guardadas).
    /// </summary>
    Task<OnvifNetworkChangeResult> SetAdminPasswordAsync(
        DiscoveredDevice device,
        string currentPassword,
        string newPassword,
        CancellationToken cancellationToken = default);
}
