using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace CameraInspector.Network;

/// <summary>
/// Sondeo TCP acotado de puertos frecuentes en cámaras, NVR y servidores de video.
/// No sustituye a los protocolos de descubrimiento; aporta evidencia cuando ICMP/ONVIF no responden.
/// </summary>
public sealed class CameraPortScanner
{
    private static readonly int[] CameraPorts =
    {
        80, 81, 82, 88, 443, 5000, 554, 8000,
        8080, 8081, 8443, 8554, 37777, 37778, 8888, 9000
    };

    public async Task<IReadOnlyList<CameraPortScanResult>> ScanAsync(
        IEnumerable<IPAddress> candidates,
        int timeoutMs = 300,
        int maxParallelism = 64,
        CancellationToken cancellationToken = default)
    {
        var found = new ConcurrentBag<CameraPortScanResult>();

        await Parallel.ForEachAsync(
            candidates.Distinct(),
            new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Clamp(maxParallelism, 1, 96),
                CancellationToken = cancellationToken
            },
            async (ip, token) =>
            {
                foreach (var port in CameraPorts)
                {
                    token.ThrowIfCancellationRequested();
                    if (await IsOpenAsync(ip, port, timeoutMs, token))
                        found.Add(new CameraPortScanResult(ip, port));
                }
            });

        return found
            .GroupBy(item => item.IpAddress)
            .Select(group => new CameraPortScanResult(
                group.Key,
                group.SelectMany(item => item.Ports).Distinct().OrderBy(port => port).ToArray()))
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
            timeout.CancelAfter(Math.Clamp(timeoutMs, 100, 1500));
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

    public bool Http => Ports.Any(port => port is 80 or 81 or 82 or 88 or 8080 or 8081 or 8888);
    public bool Https => Ports.Any(port => port is 443 or 8443);
    public bool Rtsp => Ports.Any(port => port is 554 or 8554);
}
