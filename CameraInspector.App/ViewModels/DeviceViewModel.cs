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

    public int? CameraId
    {
        get => _cameraId;
        set
        {
            if (value is null)
            {
                if (_cameraId is null) return;
                _cameraId = null;
                OnPropertyChanged();
                return;
            }

            if (value <= 0 || _cameraId == value) return;
            _cameraId = value;
            OnPropertyChanged();
        }
    }

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
            if (value) return;
            if (!string.IsNullOrWhiteSpace(_device.OnvifDeviceServiceXAddr))
                _device.OnvifDeviceServiceXAddr = null;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasMediaService));
        }
    }

    public bool HasMediaService
    {
        get => _device.HasOnvifMediaService;
        set
        {
            if (value || string.IsNullOrWhiteSpace(_device.OnvifMediaServiceXAddr)) return;
            _device.OnvifMediaServiceXAddr = null;
            OnPropertyChanged();
        }
    }

    public bool HasImagingService
    {
        get => _device.HasOnvifImagingService;
        set
        {
            if (value || string.IsNullOrWhiteSpace(_device.OnvifImagingServiceXAddr)) return;
            _device.OnvifImagingServiceXAddr = null;
            OnPropertyChanged();
        }
    }

    public bool HasPtzService
    {
        get => _device.HasOnvifPtzService;
        set
        {
            if (value || string.IsNullOrWhiteSpace(_device.OnvifPtzServiceXAddr)) return;
            _device.OnvifPtzServiceXAddr = null;
            OnPropertyChanged();
        }
    }

    public bool HasEventsService
    {
        get => _device.HasOnvifEventsService;
        set
        {
            if (value || string.IsNullOrWhiteSpace(_device.OnvifEventsServiceXAddr)) return;
            _device.OnvifEventsServiceXAddr = null;
            OnPropertyChanged();
        }
    }

    public string OnvifDeviceServiceXAddr
    {
        get => _device.OnvifDeviceServiceXAddr ?? "—";
        set
        {
            var normalized = string.Equals(value, "—", StringComparison.Ordinal) ? null : value?.Trim();
            if (string.Equals(_device.OnvifDeviceServiceXAddr, normalized, StringComparison.OrdinalIgnoreCase)) return;
            _device.OnvifDeviceServiceXAddr = normalized;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DiscoveredByOnvif));
        }
    }

    public string OnvifMediaServiceXAddr
    {
        get => _device.OnvifMediaServiceXAddr ?? "—";
        set
        {
            var normalized = string.Equals(value, "—", StringComparison.Ordinal) ? null : value?.Trim();
            if (string.Equals(_device.OnvifMediaServiceXAddr, normalized, StringComparison.OrdinalIgnoreCase)) return;
            _device.OnvifMediaServiceXAddr = normalized;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasMediaService));
        }
    }

    public string OnvifImagingServiceXAddr
    {
        get => _device.OnvifImagingServiceXAddr ?? "—";
        set
        {
            var normalized = string.Equals(value, "—", StringComparison.Ordinal) ? null : value?.Trim();
            if (string.Equals(_device.OnvifImagingServiceXAddr, normalized, StringComparison.OrdinalIgnoreCase)) return;
            _device.OnvifImagingServiceXAddr = normalized;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasImagingService));
        }
    }

    public string OnvifPtzServiceXAddr
    {
        get => _device.OnvifPtzServiceXAddr ?? "—";
        set
        {
            var normalized = string.Equals(value, "—", StringComparison.Ordinal) ? null : value?.Trim();
            if (string.Equals(_device.OnvifPtzServiceXAddr, normalized, StringComparison.OrdinalIgnoreCase)) return;
            _device.OnvifPtzServiceXAddr = normalized;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasPtzService));
        }
    }

    public string OnvifEventsServiceXAddr
    {
        get => _device.OnvifEventsServiceXAddr ?? "—";
        set
        {
            var normalized = string.Equals(value, "—", StringComparison.Ordinal) ? null : value?.Trim();
            if (string.Equals(_device.OnvifEventsServiceXAddr, normalized, StringComparison.OrdinalIgnoreCase)) return;
            _device.OnvifEventsServiceXAddr = normalized;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasEventsService));
        }
    }

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

    public string DetectionReason
    {
        get => _device.DetectionReason;
        set
        {
            // Propiedad calculada: el setter existe solo para evitar fallos TwoWay inesperados.
        }
    }

    public string DetectionDetails
    {
        get => _device.DetectionEvidence.Count == 0
            ? "Sin evidencia"
            : string.Join(Environment.NewLine, _device.DetectionEvidence
                .OrderByDescending(item => item.Confidence)
                .Select(item => $"{item.Method}: {item.Details ?? "confirmado"} · {(item.Confidence * 100):0}%"));
        set
        {
            // Propiedad calculada: el setter existe solo para evitar fallos TwoWay inesperados.
        }
    }

    /// <summary>Resumen compacto para la ficha y el diagnóstico de la cámara.</summary>
    public string TechnicalProfileSummary => string.Join(" · ", new[]
    {
        $"HTTP: {( _device.HttpSupported ? "sí" : "no")}",
        $"HTTPS: {( _device.HttpsSupported ? "sí" : "no")}",
        $"RTSP: {( _device.RtspSupported ? "sí" : "no")}",
        $"ONVIF: {( _device.OnvifSupported ? "sí" : "no")}",
        $"Media: {( _device.HasOnvifMediaService ? "sí" : "no")}",
        $"PTZ: {( _device.HasOnvifPtzService ? "sí" : "no")}",
        $"Imaging: {( _device.HasOnvifImagingService ? "sí" : "no")}",
        $"Events: {( _device.HasOnvifEventsService ? "sí" : "no")}",
    });

    public void SetCameraId(int cameraId)
    {
        if (cameraId <= 0)
            throw new ArgumentOutOfRangeException(nameof(cameraId));
        CameraId = cameraId;
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
        OnPropertyChanged(nameof(TechnicalProfileSummary));
    }
}
