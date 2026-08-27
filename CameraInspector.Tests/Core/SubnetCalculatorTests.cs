using System.Net;
using CameraInspector.Core.Models;
using CameraInspector.Network;
using Xunit;

namespace CameraInspector.Tests.Core;

public sealed class SubnetCalculatorTests
{
    [Fact]
    public void LinkLocalNoDebeGenerarBarridoDeDieciseisBits()
    {
        // networkInterface representa un adaptador Ethernet que Windows autoconfiguró con 169.254.x.x.
        var networkInterface = new NetworkInterfaceInfo
        {
            Name = "Ethernet de prueba",
            Description = "Adaptador de laboratorio",
            InterfaceId = "TEST-LINK-LOCAL",
            IpAddress = IPAddress.Parse("169.254.219.152"),
            SubnetMask = IPAddress.Parse("255.255.0.0"),
            CidrPrefixLength = 16,
            NetworkAddress = IPAddress.Parse("169.254.0.0"),
            DefaultGateway = null,
            UsesDhcp = true,
            IsUp = true,
            IsWireless = false
        };

        // calculator es la implementación que debe evitar el barrido masivo de 65.534 hosts.
        var calculator = new SubnetCalculator();

        // hosts es el resultado de candidatos de barrido que esperamos vacío para link-local.
        var hosts = calculator.GetHostAddresses(networkInterface).ToList();

        // La condición garantiza que el descubrimiento propietario/WS-Discovery pueda continuar sin un ping sweep gigante.
        Assert.Empty(hosts);
    }
}
