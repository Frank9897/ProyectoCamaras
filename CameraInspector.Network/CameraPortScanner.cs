using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace CameraInspector.Network;

/// <summary>
/// Sondeo TCP acotado de puertos típicos de cámaras/servidores de vídeo.
/// No sustituye a ONVIF: sirve para encontrar dispositivos que bloquean ICMP
/// o que no implementan WS-Discovery, como algunos modelos VIVOTEK antiguos.
/// </summary>
public sealed class CameraPortScanner
{
    // Puertos comunes de administración/streaming. Se mantienen deliberadamente acotados.
    private static readonly int[] CameraPorts =
    {
        80, 443, 554, 8080, 8000, 8081, 8554, 8888
    };

    public async Task<IReadOnlyList<CameraPortScanResult>> ScanAsync(
        IEnumerable<IPAddress> candidates,
        int timeoutMs = 350,
        int maxParallelism = 64,
        CancellationToken cancellationToken = default)
    {
        var found = new ConcurrentBag<CameraPortScanResult>();

        await Parallel.ForEachAsync(
            candidates.Distinct(),
            new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Clamp(maxParallelism, 1, 128),
                CancellationToken = cancellationToken
            },
            async (ip, token) =>
            {
                foreach (var port in CameraPorts)
                {
                    token.ThrowIfCancellationRequested();
                    if (await IsOpenAsync(ip, port, timeoutMs, token))
                    {
                        found.Add(new CameraPortScanResult(ip, port));
                    }
                }
            });

        return found
            .GroupBy(item => item.IpAddress)
            .Select(group => new CameraPortScanResult(
                group.Key,
                group.Select(item => item.Port).Distinct().OrderBy(port => port).ToArray()))
            .ToList();
    }

    private static async Task<bool> IsOpenAsync(
        IPAddress address,
        int port,
        int timeoutMs,
        CancellationToken cancellationToken)
    {
        using var client = new TcpClient(address.AddressFamily);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(Math.Clamp(timeoutMs, 100, 2000));
            await client.ConnectAsync(address, port, timeout.Token);
            return client.Connected;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (SocketException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }
}

public sealed record CameraPortScanResult(IPAddress IpAddress, IReadOnlyList<int> Ports)
{
    public CameraPortScanResult(IPAddress ipAddress, int port)
        : this(ipAddress, new[] { port })
    {
    }

    public bool Http => Ports.Contains(80) || Ports.Contains(8080) || Ports.Contains(8081) || Ports.Contains(8888);
    public bool Https => Ports.Contains(443);
    public bool Rtsp => Ports.Contains(554) || Ports.Contains(8554);
}
