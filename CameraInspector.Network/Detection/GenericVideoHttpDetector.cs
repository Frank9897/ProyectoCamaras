using System.Net.Http.Headers;
using CameraInspector.Core.Interfaces;
using CameraInspector.Core.Models;

namespace CameraInspector.Network.Detection;

/// <summary>
/// Detecta cámaras HTTP/MJPEG antiguas o genéricas sin exigir ONVIF ni un fabricante conocido.
/// Solo considera evidencia fuerte cuando un endpoint típico devuelve imagen o MJPEG.
/// </summary>
public sealed class GenericVideoHttpDetector : IManufacturerDetector
{
    private static readonly int[] DefaultPorts = { 80, 81, 82, 88, 8000, 8080, 8081, 8888 };
    private static readonly string[] Paths =
    {
        "/snapshot.jpg",
        "/snap.jpg",
        "/image.jpg",
        "/video.jpg",
        "/cgi-bin/video.jpg",
        "/mjpg/video.mjpg",
        "/video.mjpg",
        "/cgi-bin/mjpg/video.cgi"
    };

    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(700);
    private static readonly HttpClientHandler Handler = new() { AllowAutoRedirect = false };
    private static readonly HttpClient Http = new(Handler) { Timeout = Timeout };

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
            foreach (var path in Paths)
            {
                var scheme = port is 443 or 8443 ? "https" : "http";
                var result = await ProbeAsync($"{scheme}://{device.IpAddress}:{port}{path}", cancellationToken);
                if (result is null || !result.Value.IsVideo)
                    continue;

                return new ManufacturerDetectionResult
                {
                    DetectorName = Name,
                    Confidence = 0.96,
                    CameraEvidence = true,
                    EvidenceDetails = $"HTTP video endpoint: {path} (TCP/{port})",
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
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Range = new RangeHeaderValue(0, 0);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("image/jpeg"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("multipart/x-mixed-replace"));
        request.Headers.UserAgent.ParseAdd("CameraInspector/1.0");

        try
        {
            using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return new VideoProbe(false);

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
