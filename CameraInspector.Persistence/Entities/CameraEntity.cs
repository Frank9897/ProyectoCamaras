namespace CameraInspector.Persistence.Entities;

/// <summary>
/// Tabla "cameras": inventario persistente. Un DiscoveredDevice (Core) se traduce a esta
/// entidad recién cuando se decide guardarlo — no todo lo que aparece en un escaneo
/// se persiste automáticamente (podría ser ruido de red).
/// </summary>
public sealed class CameraEntity
{
    public int Id { get; set; }
    public required string Ip { get; set; }
    public string? Mac { get; set; }
    public string? Manufacturer { get; set; }
    public string? Model { get; set; }
    public string? Firmware { get; set; }
    public string? SerialNumber { get; set; }
    public string? Hostname { get; set; }

    public DateTimeOffset FirstSeen { get; set; }
    public DateTimeOffset LastSeen { get; set; }
    public DateTimeOffset? LastTest { get; set; }

    public string? Info { get; set; } // JSON libre para capacidades no tabuladas explícitamente

    public ICollection<CameraInterfaceEntity> Interfaces { get; set; } = new List<CameraInterfaceEntity>();
    public ICollection<CameraTestEntity> Tests { get; set; } = new List<CameraTestEntity>();
    public ICollection<CameraEventEntity> Events { get; set; } = new List<CameraEventEntity>();
    public CameraCredentialEntity? Credential { get; set; }
}

/// <summary>Tabla "camera_interfaces": qué protocolos/puertos expone el dispositivo.</summary>
public sealed class CameraInterfaceEntity
{
    public int Id { get; set; }
    public int CameraId { get; set; }
    public required string Protocol { get; set; } // ONVIF, RTSP, HTTP, HTTPS...
    public int? Port { get; set; }
    public bool Status { get; set; }
}

/// <summary>Tabla "camera_tests": historial de resultados de diagnóstico (Capa 6).</summary>
public sealed class CameraTestEntity
{
    public int Id { get; set; }
    public int CameraId { get; set; }
    public required string TestType { get; set; }
    public required string TestName { get; set; }
    public required string Result { get; set; } // OK / ERROR / SKIPPED
    public int? ResponseTimeMs { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset TestDate { get; set; }
}

/// <summary>Tabla "camera_events": eventos recibidos vía ONVIF Events u observados por la app.</summary>
public sealed class CameraEventEntity
{
    public int Id { get; set; }
    public int CameraId { get; set; }
    public required string EventType { get; set; }
    public string? Description { get; set; }
    public DateTimeOffset EventDate { get; set; }
}

/// <summary>
/// Tabla "camera_credentials": SOLO la referencia (GUID) a Windows Credential Manager.
/// Nunca el password ni siquiera cifrado acá — ver CameraInspector.Core.Security (Capa 9).
/// </summary>
public sealed class CameraCredentialEntity
{
    public int Id { get; set; }
    public int CameraId { get; set; }
    public required string Username { get; set; }
    public required Guid CredentialRef { get; set; }
    public DateTimeOffset? LastVerifiedAt { get; set; }
}
