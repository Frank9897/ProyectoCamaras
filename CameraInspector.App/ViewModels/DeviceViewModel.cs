using CameraInspector.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CameraInspector.App.ViewModels;

/// <summary>
/// Envoltorio de binding sobre DiscoveredDevice. Se mantiene separado del modelo de Core
/// para no mezclar lógica de negocio con detalles específicos de WPF.
/// </summary>
public sealed partial class DeviceViewModel : ObservableObject
{
    // _device mantiene la referencia al modelo de Core que contiene los datos técnicos reales.
    private readonly DiscoveredDevice _device;

    /// <summary>Acceso controlado al modelo real para que otros servicios puedan operar sobre él.</summary>
    public DiscoveredDevice Device => _device;

    public DeviceViewModel(DiscoveredDevice device) => _device = device;

    /// <summary>Dirección IP actual detectada para el dispositivo.</summary>
    public string IpAddress => _device.IpAddress;

    /// <summary>Dirección MAC aprendida por ARP; muestra un guion si todavía no existe.</summary>
    public string MacAddress => _device.MacAddress ?? "—";

    /// <summary>Fabricante identificado por la capa de detección.</summary>
    public string Manufacturer => _device.Manufacturer ?? "Sin identificar";

    /// <summary>Modelo reportado por el dispositivo.</summary>
    public string Model => _device.Model ?? "—";

    /// <summary>Versión de firmware reportada por el dispositivo.</summary>
    public string Firmware => _device.FirmwareVersion ?? "—";

    /// <summary>Indica si algún detector confirmó ONVIF.</summary>
    public bool OnvifSupported => _device.OnvifSupported;

    /// <summary>Indica si el dispositivo expuso o anunció RTSP.</summary>
    public bool RtspSupported => _device.RtspSupported;

    /// <summary>Indica si el dispositivo fue localizado mediante WS-Discovery.</summary>
    public bool DiscoveredByOnvif => !string.IsNullOrWhiteSpace(_device.OnvifDeviceServiceXAddr);

    /// <summary>URL exacta del Device Service ONVIF anunciada o detectada.</summary>
    public string OnvifDeviceServiceXAddr => _device.OnvifDeviceServiceXAddr ?? "—";

    /// <summary>Estado general derivado del último descubrimiento/diagnóstico.</summary>
    public DeviceStatus Status => _device.Status;

    /// <summary>Momento de la última detección del dispositivo.</summary>
    public DateTimeOffset LastSeenAt => _device.LastSeenAt;

    /// <summary>
    /// Notifica a la UI que los datos del dispositivo cambiaron después de una operación asíncrona.
    /// </summary>
    public void Refresh()
    {
        // Cada OnPropertyChanged obliga al DataGrid a volver a leer la propiedad correspondiente.
        OnPropertyChanged(nameof(Manufacturer));
        OnPropertyChanged(nameof(Model));
        OnPropertyChanged(nameof(Firmware));
        OnPropertyChanged(nameof(OnvifSupported));
        OnPropertyChanged(nameof(RtspSupported));
        OnPropertyChanged(nameof(DiscoveredByOnvif));
        OnPropertyChanged(nameof(OnvifDeviceServiceXAddr));
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(LastSeenAt));
    }
}
