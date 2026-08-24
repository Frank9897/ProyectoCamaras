using CameraInspector.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CameraInspector.App.ViewModels;

/// <summary>
/// Envoltorio de binding sobre DiscoveredDevice.
/// Mantiene la presentación separada del modelo de Core.
/// </summary>
public sealed partial class DeviceViewModel : ObservableObject
{
    // _device mantiene la referencia al modelo técnico real compartido por las capas.
    private readonly DiscoveredDevice _device;

    /// <summary>Acceso al modelo real para que servicios externos puedan operar sobre él.</summary>
    public DiscoveredDevice Device => _device;

    public DeviceViewModel(DiscoveredDevice device) => _device = device;

    public string IpAddress => _device.IpAddress;
    public string MacAddress => _device.MacAddress ?? "—";
    public string Manufacturer => _device.Manufacturer ?? "Sin identificar";
    public string Model => _device.Model ?? "—";
    public string Firmware => _device.FirmwareVersion ?? "—";
    public string SerialNumber => _device.SerialNumber ?? "—";

    /// <summary>Indica que un detector o servicio ONVIF confirmó compatibilidad.</summary>
    public bool OnvifSupported => _device.OnvifSupported;

    /// <summary>Indica que el dispositivo expone o anuncia RTSP.</summary>
    public bool RtspSupported => _device.RtspSupported;

    /// <summary>Indica que el dispositivo fue descubierto mediante WS-Discovery o tiene un XAddr válido.</summary>
    public bool DiscoveredByOnvif => !string.IsNullOrWhiteSpace(_device.OnvifDeviceServiceXAddr);

    /// <summary>Indica la existencia del Media Service ONVIF.</summary>
    public bool HasMediaService => _device.HasOnvifMediaService;

    /// <summary>Indica la existencia del Imaging Service ONVIF.</summary>
    public bool HasImagingService => _device.HasOnvifImagingService;

    /// <summary>Indica la existencia del PTZ Service ONVIF.</summary>
    public bool HasPtzService => _device.HasOnvifPtzService;

    /// <summary>Indica la existencia del Events Service ONVIF.</summary>
    public bool HasEventsService => _device.HasOnvifEventsService;

    /// <summary>URL exacta del Device Service ONVIF.</summary>
    public string OnvifDeviceServiceXAddr => _device.OnvifDeviceServiceXAddr ?? "—";

    /// <summary>URL exacta del Media Service ONVIF.</summary>
    public string OnvifMediaServiceXAddr => _device.OnvifMediaServiceXAddr ?? "—";

    /// <summary>URL exacta del Imaging Service ONVIF.</summary>
    public string OnvifImagingServiceXAddr => _device.OnvifImagingServiceXAddr ?? "—";

    /// <summary>URL exacta del PTZ Service ONVIF.</summary>
    public string OnvifPtzServiceXAddr => _device.OnvifPtzServiceXAddr ?? "—";

    /// <summary>URL exacta del Events Service ONVIF.</summary>
    public string OnvifEventsServiceXAddr => _device.OnvifEventsServiceXAddr ?? "—";

    public DeviceStatus Status => _device.Status;
    public DateTimeOffset LastSeenAt => _device.LastSeenAt;

    /// <summary>
    /// Notifica a WPF que el modelo técnico cambió después de una operación asíncrona.
    /// </summary>
    public void Refresh()
    {
        // Cada notificación obliga al binding correspondiente a releer el valor actualizado.
        OnPropertyChanged(nameof(Manufacturer));
        OnPropertyChanged(nameof(Model));
        OnPropertyChanged(nameof(Firmware));
        OnPropertyChanged(nameof(SerialNumber));
        OnPropertyChanged(nameof(OnvifSupported));
        OnPropertyChanged(nameof(RtspSupported));
        OnPropertyChanged(nameof(DiscoveredByOnvif));
        OnPropertyChanged(nameof(HasMediaService));
        OnPropertyChanged(nameof(HasImagingService));
        OnPropertyChanged(nameof(HasPtzService));
        OnPropertyChanged(nameof(HasEventsService));
        OnPropertyChanged(nameof(OnvifDeviceServiceXAddr));
        OnPropertyChanged(nameof(OnvifMediaServiceXAddr));
        OnPropertyChanged(nameof(OnvifImagingServiceXAddr));
        OnPropertyChanged(nameof(OnvifPtzServiceXAddr));
        OnPropertyChanged(nameof(OnvifEventsServiceXAddr));
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(LastSeenAt));
    }
}
