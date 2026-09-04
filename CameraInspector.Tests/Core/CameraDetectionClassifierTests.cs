using CameraInspector.Core.Models;
using CameraInspector.Network.Detection;

namespace CameraInspector.Tests.Core;

public sealed class CameraDetectionClassifierTests
{
    [Fact]
    public void GenericRtspOnly_DoesNotClassifyAsCamera()
    {
        var device = new DiscoveredDevice { IpAddress = "192.168.1.20" };
        device.AddEvidence("RtspFingerprint", 0.92, "RTSP/554", false);
        device.RtspSupported = true;

        var result = CameraDetectionClassifier.Classify(device);

        Assert.False(result.IsLikelyCamera);
        Assert.Equal(0, result.StrongEvidenceCount);
        Assert.True(result.WeakEvidenceCount >= 1);
    }

    [Fact]
    public void OnvifEvidence_ClassifiesAsCamera()
    {
        var device = new DiscoveredDevice
        {
            IpAddress = "192.168.1.21",
            CameraEvidence = true
        };
        device.AddEvidence("OnvifProbe", 0.98, "Device service", true);

        var result = CameraDetectionClassifier.Classify(device);

        Assert.True(result.IsLikelyCamera);
        Assert.True(result.StrongEvidenceCount >= 1);
    }

    [Fact]
    public void WeakSignalsWithoutIndependentCameraEvidence_DoNotClassifyAsCamera()
    {
        var device = new DiscoveredDevice { IpAddress = "192.168.1.22", Model = "Generic PC" };
        device.AddEvidence("Arp", 0.20, "MAC", false);
        device.AddEvidence("HttpBanner", 0.50, "Server: nginx", false);
        device.AddEvidence("SSDP", 0.55, "UPnP", false);

        var result = CameraDetectionClassifier.Classify(device);

        Assert.False(result.IsLikelyCamera);
    }

    [Fact]
    public void VivotekDiscoveryWithOnlyMac_DoesNotClassifyAsCamera()
    {
        var device = new DiscoveredDevice
        {
            IpAddress = "192.168.1.23",
            Manufacturer = "VIVOTEK",
            MacAddress = "00:11:22:33:44:55",
            CameraEvidence = true
        };
        device.AddEvidence("VivotekDiscovery", 0.95, "Vendor response", true);

        var result = CameraDetectionClassifier.Classify(device);

        Assert.False(result.IsLikelyCamera);
        Assert.Equal(0, result.StrongEvidenceCount);
    }

    [Fact]
    public void StrongAndWeakCorroboration_ClassifiesAsCamera()
    {
        var device = new DiscoveredDevice { IpAddress = "192.168.1.24" };
        device.AddEvidence("HikvisionSADP", 0.95, "SADP device", true);
        device.AddEvidence("RtspFingerprint", 0.90, "RTSP/554", false);

        var result = CameraDetectionClassifier.Classify(device);

        Assert.True(result.IsLikelyCamera);
        Assert.True(result.StrongEvidenceCount >= 1);
        Assert.True(result.WeakEvidenceCount >= 1);
    }

    [Fact]
    public void RemoteCameraFingerprint_ClassifiesAsCamera()
    {
        var device = new DiscoveredDevice
        {
            IpAddress = "203.0.113.10",
            Manufacturer = "HIKVISION",
            CameraEvidence = true
        };
        device.AddEvidence("RemoteCameraFingerprint", 0.92, "remote endpoint", true);

        var result = CameraDetectionClassifier.Classify(device);

        Assert.True(result.IsLikelyCamera);
        Assert.True(result.StrongEvidenceCount >= 1);
    }
}
