using CameraInspector.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CameraInspector.App.ViewModels;

/// <summary>
/// Envoltorio de binding sobre DiscoveredDevice. Se mantiene separado del modelo de Core
/// para no ensuciar Core con detalles de presentación (color de estado, texto formateado, etc.)
/// </summary>
public sealed partial class DeviceViewModel : ObservableObject
{
    private readonly DiscoveredDevice _device;

    /// <summary>Acceso al modelo real de Core — lo necesita MainViewModel para pasarlo a servicios (ej. IStreamUriResolver).</summary>
    public DiscoveredDevice Device => _device;

    public DeviceViewModel(DiscoveredDevice device) => _device = device;

    public string IpAddress => _device.IpAddress;
    public string MacAddress => _device.MacAddress ?? "—";
    public string Manufacturer => _device.Manufacturer ?? "Sin identificar";
    public string Model => _device.Model ?? "—";
    public string Firmware => _device.FirmwareVersion ?? "—";
    public bool OnvifSupported => _device.OnvifSupported;
    public bool RtspSupported => _device.RtspSupported;
    public DeviceStatus Status => _device.Status;
    public DateTimeOffset LastSeenAt => _device.LastSeenAt;

    /// <summary>
    /// Notifica a la UI que el DiscoveredDevice subyacente cambió (ej. tras terminar la
    /// resolución de fabricante en Capa 4). Se llama explícitamente en vez de exponer
    /// setters, porque el dueño del dato real es DiscoveredDevice, no este ViewModel.
    /// </summary>
    public void Refresh()
    {
        OnPropertyChanged(nameof(Manufacturer));
        OnPropertyChanged(nameof(Model));
        OnPropertyChanged(nameof(Firmware));
        OnPropertyChanged(nameof(OnvifSupported));
        OnPropertyChanged(nameof(RtspSupported));
        OnPropertyChanged(nameof(Status));
    }
}
