using System.Net;

namespace CameraInspector.Core.Interfaces;

/// <summary>
/// Barrido ICMP sobre un rango de direcciones IP. Responsable únicamente de decir
/// "qué IPs responden" — no resuelve MAC ni identifica fabricante (eso es de otras capas).
/// </summary>
public interface IPingScanner
{
    /// <summary>
    /// Hace ping a cada IP del rango en paralelo (acotado) y devuelve solo las que respondieron.
    /// </summary>
    Task<IReadOnlyList<IPAddress>> ScanAsync(
        IEnumerable<IPAddress> candidateAddresses,
        int timeoutMs = 300,
        int maxParallelism = 64,
        CancellationToken cancellationToken = default);
}
