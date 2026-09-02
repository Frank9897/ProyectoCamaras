using CameraInspector.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CameraInspector.App.ViewModels;

/// <summary>
/// Envoltorio de binding sobre DiscoveredDevice.
/// Mantiene la presentación separada del modelo de Core.
/// </summary>
public sealed partial class DeviceViewModel : ObservableObject
{
    private readonly DiscoveredDevice _device;
    private int? _cameraId;

    public DiscoveredDevice Device => _device;

    public DeviceViewModel(DiscoveredDevice device) => _device = device;

    public string IpAddress => _device.IpAddress;
    public string MacAddress => _device.MacAddress ?? "—";
    public string Manufacturer => _device.Manufacturer ?? "Sin identificar";
    public string Model => _device.Model ?? "—";
    public string Firmware => _device.FirmwareVersion ?? "—";
    public string SerialNumber => _device.SerialNumber ?? "—";
    public int? CameraId => _cameraId;
    public bool OnvifSupported => _device.OnvifSupported;
    public bool RtspSupported => _device.RtspSupported;
    public bool DiscoveredByOnvif => !string.IsNullOrWhiteSpace(_device.OnvifDeviceServiceXAddr);
    public bool HasMediaService => _device.HasOnvifMediaService;
    public bool HasImagingService => _device.HasOnvifImagingService;
    public bool HasPtzService => _device.HasOnvifPtzService;
    public bool HasEventsService => _device.HasOnvifEventsService;
    public string OnvifDeviceServiceXAddr => _device.OnvifDeviceServiceXAddr ?? "—";
    public string OnvifMediaServiceXAddr => _device.OnvifMediaServiceXAddr ?? "—";
    public string OnvifImagingServiceXAddr => _device.OnvifImagingServiceXAddr ?? "—";
    public string OnvifPtzServiceXAddr => _device.OnvifPtzServiceXAddr ?? "—";
    public string OnvifEventsServiceXAddr => _device.OnvifEventsServiceXAddr ?? "—";
    public DeviceStatus Status => _device.Status;
    public DateTimeOffset LastSeenAt => _device.LastSeenAt;
    public string DetectionReason => _device.DetectionReason;
    public string DetectionDetails => _device.DetectionEvidence.Count == 0
        ? "Sin evidencia"
        : string.Join(Environment.NewLine, _device.DetectionEvidence
            .OrderByDescending(item => item.Confidence)
            .Select(item => $"{item.Method}: {item.Details ?? "confirmado"} · {(item.Confidence * 100):0}%"));

    public void SetCameraId(int cameraId)
    {
        if (cameraId <= 0)
            throw new ArgumentOutOfRangeException(nameof(cameraId));
        _cameraId = cameraId;
        OnPropertyChanged(nameof(CameraId));
    }

    public void Refresh()
    {
        OnPropertyChanged(nameof(IpAddress));
        OnPropertyChanged(nameof(MacAddress));
        OnPropertyChanged(nameof(Manufacturer));
        OnPropertyChanged(nameof(Model));
        OnPropertyChanged(nameof(Firmware));
        OnPropertyChanged(nameof(SerialNumber));
        OnPropertyChanged(nameof(CameraId));
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
        OnPropertyChanged(nameof(DetectionReason));
        OnPropertyChanged(nameof(DetectionDetails));
    }
}
