using CameraInspector.Core.Interfaces;
using CameraInspector.Core.Models;

namespace CameraInspector.Network;

/// <summary>
/// Implementación de INetworkScanner. Encadena subred -> ping sweep -> ARP,
/// y va emitiendo un DiscoveredDevice por cada IP que respondió, para que la UI
/// (Capa 1) pueda ir llenando la tabla en vivo en lugar de esperar el barrido completo.
///
/// Importante: este orquestador NO identifica fabricante ni consulta ONVIF.
/// Eso es responsabilidad de la Capa 4 (resolución) y Capa 5 (providers),
/// que reciben este DiscoveredDevice "crudo" como entrada.
/// </summary>
public sealed class NetworkScanOrchestrator : INetworkScanner
{
    private readonly ISubnetCalculator _subnetCalculator;
    private readonly IPingScanner _pingScanner;
    private readonly IArpResolver _arpResolver;

    public NetworkScanOrchestrator(
        ISubnetCalculator subnetCalculator,
        IPingScanner pingScanner,
        IArpResolver arpResolver)
    {
        _subnetCalculator = subnetCalculator;
        _pingScanner = pingScanner;
        _arpResolver = arpResolver;
    }

    public async IAsyncEnumerable<ScanProgress> ScanAsync(
        NetworkInterfaceInfo networkInterface,
        IProgress<ScanProgress>? progress = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var candidates = _subnetCalculator.GetHostAddresses(networkInterface).ToList();
        int total = candidates.Count;

        var responsive = await _pingScanner.ScanAsync(candidates, cancellationToken: cancellationToken);

        // Dar un instante al SO para que termine de poblar la caché ARP tras el sweep.
        await Task.Delay(150, cancellationToken);
        var arpTable = _arpResolver.GetArpTable();

        int scanned = 0;
        foreach (var ip in responsive)
        {
            cancellationToken.ThrowIfCancellationRequested();
            scanned++;

            arpTable.TryGetValue(ip, out var mac);

            var device = new DiscoveredDevice
            {
                IpAddress = ip.ToString(),
                MacAddress = mac,
                Status = DeviceStatus.Unknown // se define recién tras el diagnóstico (Capa 6)
            };

            var update = new ScanProgress(scanned, total, device);
            progress?.Report(update);
            yield return update;
        }

        // Reporte final para que la UI sepa que el barrido terminó aunque no haya más dispositivos.
        if (responsive.Count == 0)
        {
            yield return new ScanProgress(total, total, null);
        }
    }
}
