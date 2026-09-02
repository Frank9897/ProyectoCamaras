using CameraInspector.Core.Models;

namespace CameraInspector.Tests.Core;

public sealed class DetectionEvidenceTests
{
    [Fact]
    public void AddEvidence_DeduplicatesSameMethodAndDetails_KeepingHighestConfidence()
    {
        var device = new DiscoveredDevice { IpAddress = "192.168.1.20" };

        device.AddEvidence("RTSP", 0.70, "TCP/554", true);
        device.AddEvidence("RTSP", 0.90, "TCP/554", false);

        var evidence = Assert.Single(device.DetectionEvidence);
        Assert.Equal("RTSP", evidence.Method);
        Assert.Equal(0.90, evidence.Confidence);
        Assert.True(evidence.IsCameraEvidence);
    }

    [Fact]
    public void DetectionReason_OrdersMethodsByConfidence()
    {
        var device = new DiscoveredDevice { IpAddress = "192.168.1.20" };

        device.AddEvidence("Ping", 0.05, "ICMP", false);
        device.AddEvidence("WS-Discovery", 0.98, "ONVIF", true);
        device.AddEvidence("RTSP", 0.82, "TCP/554", true);

        Assert.Equal("WS-Discovery + RTSP + Ping", device.DetectionReason);
    }
}
