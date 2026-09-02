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

    // Estas propiedades se mantienen con setter para tolerar bindings TwoWay generados por
    // DataGrid/WPF. La vista principal es de solo lectura, por lo que estos setters solo
    // sincronizan el modelo subyacente y notifican cambios cuando alguna vista los actualiza.
    public string IpAddress
    {
        get => _device.IpAddress;
        set
        {
            if (string.IsNullOrWhiteSpace(value) || string.Equals(_device.IpAddress, value, StringComparison.OrdinalIgnoreCase)) return;
            _device.IpAddress = value.Trim();
            OnPropertyChanged();
        }
    }

    public string MacAddress
    {
        get => _device.MacAddress ?? "—";
        set
        {
            var normalized = string.Equals(value, "—", StringComparison.Ordinal) ? null : value?.Trim();
            if (string.Equals(_device.MacAddress, normalized, StringComparison.OrdinalIgnoreCase)) return;
            _device.MacAddress = normalized;
            OnPropertyChanged();
        }
    }

    public string Manufacturer
    {
        get => _device.Manufacturer ?? "Sin identificar";
        set
        {
            var normalized = string.Equals(value, "Sin identificar", StringComparison.Ordinal) ? null : value?.Trim();
            if (string.Equals(_device.Manufacturer, normalized, StringComparison.OrdinalIgnoreCase)) return;
            _device.Manufacturer = normalized;
            OnPropertyChanged();
        }
    }

    public string Model
    {
        get => _device.Model ?? "—";
        set
        {
            var normalized = string.Equals(value, "—", StringComparison.Ordinal) ? null : value?.Trim();
            if (string.Equals(_device.Model, normalized, StringComparison.OrdinalIgnoreCase)) return;
            _device.Model = normalized;
            OnPropertyChanged();
        }
    }

    public string Firmware
    {
        get => _device.FirmwareVersion ?? "—";
        set
        {
            var normalized = string.Equals(value, "—", StringComparison.Ordinal) ? null : value?.Trim();
            if (string.Equals(_device.FirmwareVersion, normalized, StringComparison.OrdinalIgnoreCase)) return;
            _device.FirmwareVersion = normalized;
            OnPropertyChanged();
        }
    }

    public string SerialNumber
    {
        get => _device.SerialNumber ?? "—";
        set
        {
            var normalized = string.Equals(value, "—", StringComparison.Ordinal) ? null : value?.Trim();
            if (string.Equals(_device.SerialNumber, normalized, StringComparison.OrdinalIgnoreCase)) return;
            _device.SerialNumber = normalized;
            OnPropertyChanged();
        }
    }

    public int? CameraId => _cameraId;

    public bool OnvifSupported
    {
        get => _device.OnvifSupported;
        set
        {
            if (_device.OnvifSupported == value) return;
            _device.OnvifSupported = value;
            OnPropertyChanged();
        }
    }

    public bool RtspSupported
    {
        get => _device.RtspSupported;
        set
        {
            if (_device.RtspSupported == value) return;
            _device.RtspSupported = value;
            OnPropertyChanged();
        }
    }

    public bool DiscoveredByOnvif
    {
        get => !string.IsNullOrWhiteSpace(_device.OnvifDeviceServiceXAddr);
        set
        {
            if (!value && !string.IsNullOrWhiteSpace(_device.OnvifDeviceServiceXAddr))
                _device.OnvifDeviceServiceXAddr = null;
            OnPropertyChanged();
        }
    }

    public bool HasMediaService => _device.HasOnvifMediaService;
    public bool HasImagingService => _device.HasOnvifImagingService;
    public bool HasPtzService => _device.HasOnvifPtzService;
    public bool HasEventsService => _device.HasOnvifEventsService;
    public string OnvifDeviceServiceXAddr => _device.OnvifDeviceServiceXAddr ?? "—";
    public string OnvifMediaServiceXAddr => _device.OnvifMediaServiceXAddr ?? "—";
    public string OnvifImagingServiceXAddr => _device.OnvifImagingServiceXAddr ?? "—";
    public string OnvifPtzServiceXAddr => _device.OnvifPtzServiceXAddr ?? "—";
    public string OnvifEventsServiceXAddr => _device.OnvifEventsServiceXAddr ?? "—";

    public DeviceStatus Status
    {
        get => _device.Status;
        set
        {
            if (_device.Status == value) return;
            _device.Status = value;
            OnPropertyChanged();
        }
    }

    public DateTimeOffset LastSeenAt
    {
        get => _device.LastSeenAt;
        set
        {
            if (_device.LastSeenAt == value) return;
            _device.LastSeenAt = value;
            OnPropertyChanged();
        }
    }

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
