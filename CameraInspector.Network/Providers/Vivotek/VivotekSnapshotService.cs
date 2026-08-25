using System.Net;
using CameraInspector.Core.Interfaces;

namespace CameraInspector.Network.Providers.Vivotek;

/// <summary>
/// Implementación del snapshot propietario de VIVOTEK mediante viewer/video.jpg.
/// </summary>
public sealed class VivotekSnapshotService : IVivotekSnapshotService
{
    private readonly TimeSpan _timeout = TimeSpan.FromSeconds(6);

    public async Task<bool> SaveSnapshotAsync(
        string ipAddress,
        string username,
        string password,
        string filePath,
        int? channel = null,
        int? resolution = null,
        int? quality = null,
        CancellationToken cancellationToken = default)
    {
        // ipAddress identifica la cámara VIVOTEK a la que se solicitará el frame.
        ArgumentException.ThrowIfNullOrWhiteSpace(ipAddress);
        // filePath determina dónde se guardará localmente el JPEG recibido.
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        using var handler = new HttpClientHandler
        {
            // Credentials contiene únicamente las credenciales proporcionadas para esta operación explícita.
            Credentials = new NetworkCredential(username, password),
            // PreAuthenticate queda desactivado para permitir que el servidor elija el desafío HTTP correspondiente.
            PreAuthenticate = false,
            // No seguimos redirecciones para evitar enviar credenciales a otro destino.
            AllowAutoRedirect = false
        };

        using var client = new HttpClient(handler)
        {
            Timeout = _timeout
        };

        // parameters conserva únicamente las opciones de snapshot que el usuario haya especificado.
        var parameters = new List<string>();

        if (channel is int channelValue)
            parameters.Add($"channel={channelValue}");

        if (resolution is int resolutionValue)
            parameters.Add($"resolution={resolutionValue}");

        if (quality is int qualityValue)
            parameters.Add($"quality={qualityValue}");

        // query concatena los parámetros opcionales sin alterar la ruta CGI documentada.
        var query = parameters.Count == 0
            ? string.Empty
            : "?" + string.Join("&", parameters);

        // endpoint solicita una sola imagen JPEG desde la API propietaria de VIVOTEK.
        var endpoint = $"http://{ipAddress.Trim()}/cgi-bin/viewer/video.jpg{query}";

        using var response = await client.GetAsync(endpoint, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return false;

        // contentType permite rechazar respuestas HTML/XML de error que hayan devuelto HTTP 200.
        var contentType = response.Content.Headers.ContentType?.MediaType;
        if (!string.IsNullOrWhiteSpace(contentType)
            && !contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // data contiene el JPEG que se copiará al archivo solicitado por el técnico.
        var data = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        if (data.Length == 0)
            return false;

        // El directorio se crea para permitir guardar el snapshot aunque el usuario haya elegido una carpeta nueva.
        var directory = Path.GetDirectoryName(Path.GetFullPath(filePath));
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        // Guardamos una copia local del frame sin conservarlo en memoria después de la operación.
        await File.WriteAllBytesAsync(filePath, data, cancellationToken);
        return true;
    }
}
