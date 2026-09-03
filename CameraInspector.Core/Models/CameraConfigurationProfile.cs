namespace CameraInspector.Core.Models;

/// <summary>
/// Describe cómo debe presentar y priorizar Camera Inspector la administración de una cámara según fabricante.
/// No afirma que una operación propietaria esté implementada; indica qué flujo es recomendado y qué capacidades se conocen.
/// </summary>
public sealed record CameraConfigurationProfile(
    string Manufacturer,
    string ProfileName,
    string DiscoveryTool,
    string ManagementStyle,
    string PrimaryProtocol,
    bool SupportsDhcp,
    bool SupportsStaticIpv4,
    bool SupportsGateway,
    bool SupportsDns,
    bool SupportsHostname,
    bool SupportsNtp,
    bool SupportsPorts,
    bool SupportsCredentials,
    bool SupportsReboot,
    bool SupportsFactoryReset,
    string Description,
    IReadOnlyList<string> RecommendedActions);
