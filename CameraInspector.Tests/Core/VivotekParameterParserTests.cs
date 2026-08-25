using CameraInspector.Network.Providers.Vivotek;

namespace CameraInspector.Tests.Core;

public sealed class VivotekParameterParserTests
{
    [Fact]
    public void Parse_debe_convertir_lineas_clave_valor_en_parametros()
    {
        // body simula exactamente el formato textual simple que utiliza el CGI de VIVOTEK.
        const string body = "modelname=IB9368-HT\nfirmwareversion=0100b\nbrightness=50\n";

        // result contiene los parámetros interpretados sin depender de una cámara física.
        var result = VivotekParameterService.Parse("image", body);

        Assert.Equal(3, result.Count);
        Assert.Equal("image", result[0].Group);
        Assert.Equal("modelname", result[0].Name);
        Assert.Equal("IB9368-HT", result[0].Value);
        Assert.Equal("50", result[2].Value);
    }

    [Fact]
    public void Parse_debe_ignorar_lineas_sin_separador()
    {
        // Las líneas inválidas representan encabezados o mensajes que algunos firmwares pueden devolver.
        const string body = "resultado\nbrightness=50\n\ninvalid\ncontrast=60\n";

        // result solo contiene las líneas con estructura clave=valor.
        var result = VivotekParameterService.Parse("image", body);

        Assert.Collection(
            result,
            item => Assert.Equal(("brightness", "50"), (item.Name, item.Value)),
            item => Assert.Equal(("contrast", "60"), (item.Name, item.Value)));
    }
}
