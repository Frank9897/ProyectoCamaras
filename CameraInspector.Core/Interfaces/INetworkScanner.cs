using System.Net;
using CameraInspector.Core.Models;

namespace CameraInspector.Core.Interfaces;

/// <summary>
/// Progreso incremental del escaneo, para que la UI pueda ir agregando filas a la tabla
/// a medida que se descubren dispositivos, en lugar de esperar a que termine todo el barrido.
/// </summary>
public sealed record ScanProgress(int Scanned, int Total, DiscoveredDevice? NewlyFound);

/// <summary>
/// Orquesta el flujo completo de descubrimiento y permite elegir si la búsqueda es directa,
/// limitada a una subred o distribuida entre todas las interfaces activas.
/// </summary>
public interface INetworkScanner
{
    IAsyncEnumerable<ScanProgress> ScanAsync(
        NetworkInterfaceInfo networkInterface,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default,
        DiscoveryScanMode mode = DiscoveryScanMode.NetworkSubnet,
        IPAddress? directAddress = null);
}
