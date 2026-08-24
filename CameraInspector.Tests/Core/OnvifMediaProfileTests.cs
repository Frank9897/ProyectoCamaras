using CameraInspector.Core.Models;

namespace CameraInspector.Tests.Core;

public sealed class OnvifMediaProfileTests
{
    [Fact]
    public void ResolutionPixels_DebeCalcularPixelesCorrectamente()
    {
        // profile representa un perfil de video con resolución conocida.
        var profile = new OnvifMediaProfile
        {
            Token = "perfil-1",
            Width = 1920,
            Height = 1080
        };

        // 1920*1080 debe producir la cantidad total de píxeles del frame.
        Assert.Equal(2_073_600, profile.ResolutionPixels);
    }

    [Fact]
    public void ResolutionPixels_DebeSerCeroCuandoFaltaResolucion()
    {
        // profile omite ancho y alto porque algunas respuestas ONVIF pueden no informar ambos valores.
        var profile = new OnvifMediaProfile
        {
            Token = "perfil-2"
        };

        Assert.Equal(0, profile.ResolutionPixels);
    }
}
