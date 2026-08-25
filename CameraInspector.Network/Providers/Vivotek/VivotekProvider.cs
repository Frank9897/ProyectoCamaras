using System.Net;
using CameraInspector.Core.Interfaces;
using CameraInspector.Core.Models;

namespace CameraInspector.Network.Providers.Vivotek;

/// <summary>
/// Provider propietario de VIVOTEK mediante CGI.
/// Esta primera versión solo realiza lectura de información básica del dispositivo.
/// </summary>
public sealed class VivotekProvider : ICameraProvider
{
    /// <summary>
    /// Timeout corto porque la consulta de identificación no debe bloquear la UI durante mucho tiempo.
    /// </summary>
    private readonly TimeSpan _timeout = TimeSpan.FromSeconds(4);

    public string Name => "VIVOTEK CGI";

    public bool CanHandle(DiscoveredDevice device)
    {
        // manufacturer y model son evidencias obtenidas antes de autenticarnos.
        var manufacturer = device.Manufacturer ?? string.Empty;
        var model = device.Model ?? string.Empty;

        // Aceptamos las variantes habituales de escritura del fabricante.
        return manufacturer.Contains("VIVOTEK", StringComparison.OrdinalIgnoreCase)
            || manufacturer.Contains("Vivotek", StringComparison.OrdinalIgnoreCase)
            || model.Contains("VIVOTEK", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<CameraProviderInfo?> GetDeviceInfoAsync(
        DiscoveredDevice device,
        string username,
        string password,
        CancellationToken cancellationToken = default)
    {
        // La IP es imprescindible porque VIVOTEK expone el CGI directamente sobre HTTP/HTTPS.
        if (string.IsNullOrWhiteSpace(device.IpAddress))
            return null;

        // HttpClientHandler permite responder a desafíos de autenticación HTTP usando las credenciales
        // proporcionadas explícitamente por el técnico. No se autentica durante el descubrimiento.
        using var handler = new HttpClientHandler
        {
            Credentials = new NetworkCredential(username, password),
            PreAuthenticate = false,
            AllowAutoRedirect = false
        };

        using var client = new HttpClient(handler)
        {
            Timeout = _timeout
        };

        // endpoint consulta la información básica del servidor mediante CGI.
        // VIVOTEK documenta /cgi-bin/sysinfo.cgi para esta finalidad.
        var endpoint = $"http://{device.IpAddress.Trim()}/cgi-bin/sysinfo.cgi";

        using var response = await client.GetAsync(endpoint, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return null;

        // responseText contiene pares clave=valor separados por líneas.
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(responseText))
            return null;

        var values = ParseKeyValueResponse(responseText);

        // Model identifica el modelo que el firmware expone por el CGI.
        var model = GetValue(values, "Model");
        // CapVersion identifica la versión de capacidades del CGI, no la versión del firmware.
        // Se conserva como evidencia local, pero no se asigna a FirmwareVersion porque semánticamente son datos distintos.
        _ = GetValue(values, "CapVersion");

        return new CameraProviderInfo
        {
            ProviderName = Name,
            Manufacturer = "VIVOTEK",
            Model = model,
            FirmwareVersion = null,
            SerialNumber = null,
            MacAddress = null,
            DeviceType = "Network Camera"
        };
    }

    /// <summary>
    /// Convierte la respuesta de texto de VIVOTEK en pares clave=valor.
    /// Se ignoran líneas sin '=' para tolerar encabezados o texto adicional del firmware.
    /// </summary>
    private static Dictionary<string, string> ParseKeyValueResponse(string responseText)
    {
        // values conserva únicamente los parámetros válidos de la respuesta.
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in responseText.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            // separator separa la clave del valor únicamente en el primer '=' encontrado.
            var separator = line.IndexOf('=');
            if (separator <= 0 || separator >= line.Length - 1)
                continue;

            // key identifica el nombre del parámetro CGI.
            var key = line[..separator].Trim();
            // value contiene el texto informado por la cámara.
            var value = line[(separator + 1)..].Trim();

            if (!string.IsNullOrWhiteSpace(key))
                values[key] = value;
        }

        return values;
    }

    /// <summary>
    /// Obtiene un parámetro ignorando diferencias de mayúsculas/minúsculas.
    /// </summary>
    private static string? GetValue(IReadOnlyDictionary<string, string> values, string key)
    {
        return values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : null;
    }
}
