using CameraInspector.Core.Models;

namespace CameraInspector.App.ViewModels;

public sealed partial class DeviceViewModel
{
    public CameraHealthState HealthState => _device.HealthState;
    public bool CommunicationAvailable => _device.CommunicationAvailable;
    public bool VideoAvailable => _device.VideoAvailable;
    public bool AuthenticationRequired => _device.AuthenticationRequired;
    public int? CommunicationPort => _device.CommunicationPort;
    public string CommunicationProtocol => _device.CommunicationProtocol ?? "—";
    public string HealthMessage => _device.HealthMessage ?? "Sin comprobación de salud";
    public DateTimeOffset? LastHealthCheckAt => _device.LastHealthCheckAt;

    public string HealthDisplay => _device.HealthState switch
    {
        CameraHealthState.Healthy => "OK",
        CameraHealthState.AuthenticationRequired => "AUTENTICACIÓN",
        CameraHealthState.CommunicationOnly => "OK · VIDEO CONFIRMADO",
        CameraHealthState.NoVideo => "ALERTA · SIN VIDEO",
        CameraHealthState.NoResponse => "ALERTA · SIN RESPUESTA",
        CameraHealthState.Degraded => "ALERTA · DEGRADADA",
        CameraHealthState.Unsupported => "NO SOPORTADA",
        _ => "PENDIENTE"
    };

    public string CommunicationDisplay => _device.CommunicationAvailable ? "OK" : "VIDEO CONFIRMADO";
    public string VideoDisplay => _device.VideoAvailable ? "OK" : "SIN VIDEO";
    public string AlertDisplay => _device.HealthState switch
    {
        CameraHealthState.Healthy => "—",
        CameraHealthState.Unknown => "PENDIENTE",
        CameraHealthState.AuthenticationRequired => "REQUIERE CREDENCIALES",
        CameraHealthState.CommunicationOnly => "VIDEO CONFIRMADO · COMUNICACIÓN DETALLADA PENDIENTE",
        CameraHealthState.NoVideo => "RESPONDE · VIDEO NO DISPONIBLE",
        CameraHealthState.NoResponse => "CÁMARA SIN RESPUESTA",
        CameraHealthState.Degraded => "SERVICIO PARCIAL",
        CameraHealthState.Unsupported => "NO SOPORTADA",
        _ => "ALERTA"
    };

    /// <summary>
    /// La reproducción real de LibVLC es evidencia directa de que el stream existe.
    /// Corrige cualquier resultado previo de una comprobación ligera que haya quedado obsoleto.
    /// </summary>
    public void ConfirmVideoAvailable(string message, int? port = null)
    {
        _device.VideoAvailable = true;
        _device.RtspSupported = true;
        _device.AuthenticationRequired = false;
        _device.Status = DeviceStatus.Online;
        _device.CommunicationPort = port ?? _device.CommunicationPort;
        _device.CommunicationProtocol = "RTSP";
        _device.HealthMessage = message;
        _device.LastHealthCheckAt = DateTimeOffset.Now;

        // Un vídeo RTSP reproducido correctamente es evidencia de comunicación funcional.
        // Esto evita mostrar "SIN VIDEO" mientras la ventana está reproduciendo la cámara.
        _device.CommunicationAvailable = true;
        _device.HealthState = CameraHealthState.Healthy;

        RefreshHealth();
    }

    public void RefreshHealth()
    {
        OnPropertyChanged(nameof(HealthState));
        OnPropertyChanged(nameof(HealthDisplay));
        OnPropertyChanged(nameof(CommunicationAvailable));
        OnPropertyChanged(nameof(CommunicationDisplay));
        OnPropertyChanged(nameof(VideoAvailable));
        OnPropertyChanged(nameof(VideoDisplay));
        OnPropertyChanged(nameof(AuthenticationRequired));
        OnPropertyChanged(nameof(CommunicationPort));
        OnPropertyChanged(nameof(CommunicationProtocol));
        OnPropertyChanged(nameof(HealthMessage));
        OnPropertyChanged(nameof(LastHealthCheckAt));
        OnPropertyChanged(nameof(AlertDisplay));
    }
}
