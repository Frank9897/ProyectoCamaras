using CameraInspector.Core.Models;

namespace CameraInspector.Tests.Core;

public sealed class OnvifPtzMoveRequestTests
{
    [Fact]
    public void ValoresIniciales_DebenRepresentarReposo()
    {
        // request comienza sin movimiento para que una instancia nueva no solicite ningún eje por accidente.
        var request = new OnvifPtzMoveRequest();

        Assert.Equal(0f, request.Pan);
        Assert.Equal(0f, request.Tilt);
        Assert.Equal(0f, request.Zoom);
    }

    [Fact]
    public void PuedeRepresentarMovimientoHorizontal()
    {
        // request representa un movimiento a la derecha con velocidad normalizada del 50 por ciento.
        var request = new OnvifPtzMoveRequest
        {
            Pan = 0.5f
        };

        Assert.Equal(0.5f, request.Pan);
        Assert.Equal(0f, request.Tilt);
        Assert.Equal(0f, request.Zoom);
    }
}
