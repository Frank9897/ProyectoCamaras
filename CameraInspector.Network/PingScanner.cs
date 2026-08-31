using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using CameraInspector.Core.Interfaces;

namespace CameraInspector.Network;

/// <summary>
/// Ping sweep con paralelismo acotado y ejecución por lotes.
/// Evita crear una Task independiente para cada IP y reduce el consumo de memoria en redes grandes.
/// </summary>
public sealed class PingScanner : IPingScanner
{
    public async Task<IReadOnlyList<IPAddress>> ScanAsync(
        IEnumerable<IPAddress> candidateAddresses,
        int timeoutMs = 300,
        int maxParallelism = 64,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidateAddresses);

        var found = new ConcurrentBag<IPAddress>();
        var addresses = candidateAddresses as IReadOnlyCollection<IPAddress>
            ?? candidateAddresses.ToArray();

        if (addresses.Count == 0)
            return Array.Empty<IPAddress>();

        timeoutMs = Math.Clamp(timeoutMs, 100, 2000);
        maxParallelism = Math.Clamp(maxParallelism, 1, 64);

        var options = new ParallelOptions
        {
            CancellationToken = cancellationToken,
            MaxDegreeOfParallelism = maxParallelism
        };

        await Parallel.ForEachAsync(addresses, options, async (ip, token) =>
        {
            try
            {
                using var ping = new Ping();
                var reply = await ping.SendPingAsync(ip, timeoutMs);
                if (reply.Status == IPStatus.Success)
                    found.Add(ip);
            }
            catch (PingException)
            {
                // IP inalcanzable o adaptador sin ruta: se ignora.
            }
            catch (InvalidOperationException)
            {
                // El adaptador puede desaparecer durante un escaneo; una dirección no debe abortar todo el pipeline.
            }
        });

        return found
            .Distinct()
            .OrderBy(ip => ip.GetAddressBytes(), ByteArrayComparer.Instance)
            .ToList();
    }

    private sealed class ByteArrayComparer : IComparer<byte[]>
    {
        public static readonly ByteArrayComparer Instance = new();

        public int Compare(byte[]? x, byte[]? y)
        {
            if (ReferenceEquals(x, y))
                return 0;
            if (x is null)
                return -1;
            if (y is null)
                return 1;

            var length = Math.Min(x.Length, y.Length);
            for (var i = 0; i < length; i++)
            {
                var comparison = x[i].CompareTo(y[i]);
                if (comparison != 0)
                    return comparison;
            }

            return x.Length.CompareTo(y.Length);
        }
    }
}
