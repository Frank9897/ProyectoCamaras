using CameraInspector.Core.Models;

namespace CameraInspector.Core.Interfaces;

/// <summary>
/// Progreso incremental del escaneo, para que la UI pueda ir agregando filas a la tabla
/// a medida que se descubren dispositivos, en lugar de esperar a que termine todo el barrido.
/// </summary>
public sealed record ScanProgress(int Scanned, int Total, DiscoveredDevice? NewlyFound);

/// <summary>
/// Orquesta el flujo completo de la Capa 3 (Descubrimiento):
/// interfaz de red -> subred -> ping sweep -> resolución ARP -> lista de DiscoveredDevice.
/// La identificación de fabricante (Capa 4) y la consulta de capacidades (Capa 5) NO ocurren acá;
/// este servicio solo entrega "qué hay" con lo mínimo (IP, MAC), no "qué es".
/// </summary>
public interface INetworkScanner
{
    IAsyncEnumerable<ScanProgress> ScanAsync(
        NetworkInterfaceInfo networkInterface,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
