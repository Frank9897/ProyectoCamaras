using System.Net.Http.Headers;
using CameraInspector.Core.Interfaces;
using CameraInspector.Core.Models;

namespace CameraInspector.Network.Detection;

/// <summary>
/// Detecta cámaras HTTP/MJPEG antiguas o genéricas sin exigir ONVIF ni un fabricante conocido.
/// Solo considera evidencia fuerte cuando un endpoint típico devuelve un Content-Type de imagen o MJPEG.
/// </summary>
public sealed class GenericVideoHttpDetector : IManufacturerDetector
{
    private static readonly int[] DefaultPorts = { 80, 81, 82, 88, 8080, 8081, 8000, 8888 };
    private static readonly string[] Paths =
    {
        "/cgi-bin/video.jpg",
        "/snapshot.jpg",
        "/snap.jpg",
        "/image.jpg",
        "/video.jpg",
        "/mjpg/video.mjpg",
        "/video.mjpg",
        "/cgi-bin/mjpg/video.cgi",
        "/cgi-bin/mjpeg"
    };

    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(900);

    public string Name => "GenericVideoHttp";

    public async Task<ManufacturerDetectionResult?> TryDetectAsync(
        DiscoveredDevice device,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(device.IpAddress))
            return null;

        var ports = new List<int>();
        if (device.HttpPort is int known)
            ports.Add(known);
        ports.AddRange(DefaultPorts);

        foreach (var port in ports.Distinct())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var scheme = port is 443 or 8443 ? "https" : "http";

            foreach (var path in Paths)
            {
                var result = await ProbeAsync($"{scheme}://{device.IpAddress}:{port}{path}", cancellationToken);
                if (result is null)
                    continue;

                if (!result.Value.IsVideo)
                    continue;

                return new ManufacturerDetectionResult
                {
                    DetectorName = Name,
                    Confidence = 0.96,
                    CameraEvidence = true,
                    Manufacturer = null,
                    Model = null,
                    HttpSupported = true,
                    HttpPort = port,
                    RtspSupported = device.RtspSupported,
                    RtspPort = device.RtspPort
                };
            }
        }

        return null;
    }

    private static async Task<VideoProbe?> ProbeAsync(string url, CancellationToken cancellationToken)
    {
        using var handler = new HttpClientHandler { AllowAutoRedirect = false };
        using var client = new HttpClient(handler) { Timeout = Timeout };
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 0);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("image/jpeg"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("multipart/x-mixed-replace"));
        request.Headers.UserAgent.ParseAdd("CameraInspector/1.0");

        try
        {
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            var contentType = response.Content.Headers.ContentType?.MediaType;
            var isVideo = contentType is not null &&
                (contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase) ||
                 contentType.Contains("multipart/x-mixed-replace", StringComparison.OrdinalIgnoreCase));
            return new VideoProbe(isVideo);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    private readonly record struct VideoProbe(bool IsVideo);
}
