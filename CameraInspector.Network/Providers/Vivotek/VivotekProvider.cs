using System.Net;
using CameraInspector.Core.Interfaces;
using CameraInspector.Core.Models;

namespace CameraInspector.Network.Providers.Vivotek;

/// <summary>
/// Provider propietario de VIVOTEK mediante CGI.
/// Primero intenta consultar system.info mediante getparam.cgi y deja sysinfo.cgi como compatibilidad secundaria.
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

        // Primer intento: API CGI moderna de lectura del grupo system.info.
        // VIVOTEK documenta getparam.cgi para consultar grupos y system.info expone modelo,
        // número de serie y firmware en varias generaciones de cámaras.
        var modernEndpoint = $"http://{device.IpAddress.Trim()}/cgi-bin/anonymous/getparam.cgi?system.info";
        var modernResponse = await TryGetAsync(client, modernEndpoint, cancellationToken);

        if (modernResponse is not null)
        {
            var values = ParseKeyValueResponse(modernResponse);
            var modernInfo = BuildModernInfo(values);

            if (modernInfo is not null)
                return modernInfo;
        }

        // Segundo intento: API sysinfo clásica para firmware que todavía la exponen.
        var legacyEndpoint = $"http://{device.IpAddress.Trim()}/cgi-bin/sysinfo.cgi";
        var legacyResponse = await TryGetAsync(client, legacyEndpoint, cancellationToken);

        if (legacyResponse is null)
            return null;

        var legacyValues = ParseKeyValueResponse(legacyResponse);
        var model = GetValue(legacyValues, "Model");
        var capabilityVersion = GetValue(legacyValues, "CapVersion");

        return new CameraProviderInfo
        {
            ProviderName = string.IsNullOrWhiteSpace(capabilityVersion)
                ? Name
                : $"{Name} (CapVersion {capabilityVersion})",
            Manufacturer = "VIVOTEK",
            Model = model,
            FirmwareVersion = null,
            SerialNumber = null,
            MacAddress = null,
            DeviceType = "Network Camera"
        };
    }

    /// <summary>
    /// Ejecuta una petición GET y devuelve el cuerpo de texto solamente ante una respuesta HTTP exitosa.
    /// </summary>
    private static async Task<string?> TryGetAsync(
        HttpClient client,
        string endpoint,
        CancellationToken cancellationToken)
    {
        // response contiene el resultado HTTP de la cámara para el endpoint consultado.
        using var response = await client.GetAsync(endpoint, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return null;

        // body contiene la respuesta textual CGI que después será convertida a pares clave=valor.
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        return string.IsNullOrWhiteSpace(body) ? null : body;
    }

    /// <summary>
    /// Construye la información común a partir del grupo system.info.
    /// </summary>
    private CameraProviderInfo? BuildModernInfo(IReadOnlyDictionary<string, string> values)
    {
        // modelName puede venir como system.info_modelname o como modelname dependiendo de la generación.
        var modelName = GetFirstValue(values,
            "modelname",
            "system.info_modelname");

        // extendedModelName conserva el nombre de producto/ODM cuando la cámara lo expone.
        var extendedModelName = GetFirstValue(values,
            "extendedmodelname",
            "system.info_extendedmodelname");

        // serialNumber es documentado por VIVOTEK como la MAC de producto sin separadores en varias generaciones.
        var serialNumber = GetFirstValue(values,
            "serialnumber",
            "system.info_serialnumber");

        // firmwareVersion contiene la versión de firmware en el formato definido por VIVOTEK.
        var firmwareVersion = GetFirstValue(values,
            "firmwareversion",
            "system.info_firmwareversion");

        if (string.IsNullOrWhiteSpace(modelName)
            && string.IsNullOrWhiteSpace(extendedModelName)
            && string.IsNullOrWhiteSpace(serialNumber)
            && string.IsNullOrWhiteSpace(firmwareVersion))
        {
            return null;
        }

        return new CameraProviderInfo
        {
            ProviderName = Name,
            Manufacturer = "VIVOTEK",
            Model = string.IsNullOrWhiteSpace(extendedModelName) ? modelName : extendedModelName,
            FirmwareVersion = firmwareVersion,
            SerialNumber = serialNumber,
            MacAddress = NormalizeMac(serialNumber),
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
    /// Obtiene el primer parámetro disponible entre varias variantes de nombre.
    /// </summary>
    private static string? GetFirstValue(
        IReadOnlyDictionary<string, string> values,
        params string[] keys)
    {
        foreach (var key in keys)
        {
            var value = GetValue(values, key);
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        return null;
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

    /// <summary>
    /// Normaliza una MAC de 12 caracteres a un formato legible cuando el valor realmente parece una MAC.
    /// </summary>
    private static string? NormalizeMac(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var compact = value.Replace(":", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Trim();

        if (compact.Length != 12 || compact.Any(character => !Uri.IsHexDigit(character)))
            return null;

        return string.Join(":", Enumerable.Range(0, 6).Select(index => compact.Substring(index * 2, 2).ToUpperInvariant()));
    }
}
