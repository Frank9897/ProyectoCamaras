using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using CameraInspector.Core.Interfaces;

namespace CameraInspector.Network;

/// <summary>
/// Ping sweep con concurrencia acotada por SemaphoreSlim: importante para no saturar
/// la NIC del técnico ni disparar alertas de escaneo en switches administrados.
/// </summary>
public sealed class PingScanner : IPingScanner
{
    public async Task<IReadOnlyList<IPAddress>> ScanAsync(
        IEnumerable<IPAddress> candidateAddresses,
        int timeoutMs = 300,
        int maxParallelism = 64,
        CancellationToken cancellationToken = default)
    {
        var found = new ConcurrentBag<IPAddress>();
        using var semaphore = new SemaphoreSlim(maxParallelism);

        var tasks = candidateAddresses.Select(async ip =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                using var ping = new Ping();
                var reply = await ping.SendPingAsync(ip, timeoutMs);
                if (reply.Status == IPStatus.Success)
                {
                    found.Add(ip);
                }
            }
            catch (PingException)
            {
                // IP inalcanzable o adaptador sin ruta: se ignora, no es un error de la app.
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);
        return found.ToList();
    }
}
