using CameraInspector.Core.Models;
using CameraInspector.Network.Configuration;

namespace CameraInspector.Tests;

public sealed class CameraConfigurationProfileTests
{
    [Fact]
    public void Vivotek_uses_wizard_style_profile()
    {
        var device = new DiscoveredDevice
        {
            IpAddress = "192.168.1.50",
            Manufacturer = "VIVOTEK",
            Model = "IP7133"
        };

        var profile = CameraConfigurationProfileResolver.Resolve(device);

        Assert.Equal("VIVOTEK", profile.Manufacturer);
        Assert.Contains("Wizard", profile.ProfileName, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Shepherd", profile.DiscoveryTool, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Hikvision_uses_sadp_profile()
    {
        var device = new DiscoveredDevice { Manufacturer = "Hikvision", Model = "DS-2CD" };

        var profile = CameraConfigurationProfileResolver.Resolve(device);

        Assert.Equal("HIKVISION", profile.Manufacturer);
        Assert.Contains("SADP", profile.DiscoveryTool, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Unknown_manufacturer_falls_back_to_generic()
    {
        var device = new DiscoveredDevice { Manufacturer = "Fabricante Desconocido", Model = "IPC-X" };

        var profile = CameraConfigurationProfileResolver.Resolve(device);

        Assert.Equal("GENÉRICO", profile.Manufacturer);
        Assert.Contains("ONVIF", profile.PrimaryProtocol, StringComparison.OrdinalIgnoreCase);
    }
}
