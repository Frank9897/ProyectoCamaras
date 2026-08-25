using System.Globalization;
using System.Text;
using CameraInspector.Core.Models;

namespace CameraInspector.Core.Services;

/// <summary>
/// Generador de reportes CSV sin dependencia de WPF, EF Core ni infraestructura externa.
/// La UI decide dónde guardar el texto generado.
/// </summary>
public static class CsvExportService
{
    /// <summary>
    /// Genera un inventario técnico completo a partir de los dispositivos visibles como cámaras.
    /// </summary>
    public static string ExportInventory(IEnumerable<DiscoveredDevice> devices)
    {
        var builder = CreateBuilder();

        // Las cabeceras describen exactamente las columnas que recibirá el técnico en Excel.
        AppendRow(builder,
            "IP",
            "MAC",
            "Hostname",
            "Fabricante",
            "Modelo",
            "Firmware",
            "Serie",
            "Estado",
            "ONVIF",
            "RTSP",
            "HTTP",
            "HTTPS",
            "Puerto HTTP",
            "Puerto RTSP",
            "Media ONVIF",
            "Imaging ONVIF",
            "PTZ ONVIF",
            "Eventos ONVIF",
            "Provider",
            "Primera detección UTC",
            "Última detección UTC");

        foreach (var device in devices)
        {
            // device contiene únicamente información técnica; nunca exportamos credenciales ni secretos.
            AppendRow(builder,
                device.IpAddress,
                device.MacAddress,
                device.Hostname,
                device.Manufacturer,
                device.Model,
                device.FirmwareVersion,
                device.SerialNumber,
                device.Status.ToString(),
                BoolText(device.OnvifSupported),
                BoolText(device.RtspSupported),
                BoolText(device.HttpSupported),
                BoolText(device.HttpsSupported),
                device.HttpPort?.ToString(CultureInfo.InvariantCulture),
                device.RtspPort?.ToString(CultureInfo.InvariantCulture),
                BoolText(device.HasOnvifMediaService),
                BoolText(device.HasOnvifImagingService),
                BoolText(device.HasOnvifPtzService),
                BoolText(device.HasOnvifEventsService),
                device.AssignedProviderName,
                device.FirstSeenAt.ToString("O", CultureInfo.InvariantCulture),
                device.LastSeenAt.ToString("O", CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }

    /// <summary>
    /// Genera el historial de diagnóstico de una cámara seleccionada.
    /// </summary>
    public static string ExportDiagnosticHistory(
        IEnumerable<DiagnosticHistoryItem> history,
        DiscoveredDevice? device = null)
    {
        var builder = CreateBuilder();

        AppendRow(builder,
            "IP",
            "MAC",
            "Fabricante",
            "Modelo",
            "Prueba",
            "Resultado",
            "Tiempo de respuesta ms",
            "Mensaje",
            "Fecha UTC");

        foreach (var item in history)
        {
            // item contiene el resultado persistido de cada prueba ejecutada sobre la cámara.
            AppendRow(builder,
                device?.IpAddress,
                device?.MacAddress,
                device?.Manufacturer,
                device?.Model,
                item.TestName,
                item.Result,
                item.ResponseTimeMs?.ToString(CultureInfo.InvariantCulture),
                item.Message,
                item.TestDate.ToString("O", CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }

    /// <summary>
    /// Crea el buffer usando StringBuilder para evitar concatenaciones repetitivas.
    /// </summary>
    private static StringBuilder CreateBuilder() => new();

    /// <summary>
    /// Agrega una fila CSV aplicando escape RFC 4180 de forma conservadora.
    /// </summary>
    private static void AppendRow(StringBuilder builder, params string?[] values)
    {
        // row contiene las columnas ya escapadas para que comas, comillas o saltos de línea no rompan el CSV.
        var row = string.Join(",", values.Select(Escape));
        builder.AppendLine(row);
    }

    /// <summary>
    /// Escapa un valor CSV encerrándolo entre comillas cuando contiene caracteres especiales.
    /// </summary>
    private static string Escape(string? value)
    {
        // text normaliza null a una celda vacía para simplificar el consumo en Excel.
        var text = value ?? string.Empty;

        if (!text.Contains(',')
            && !text.Contains('"')
            && !text.Contains('\r')
            && !text.Contains('\n'))
        {
            return text;
        }

        // Las comillas internas se duplican según el formato CSV estándar.
        return $"\"{text.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }

    private static string BoolText(bool value) => value ? "SI" : "NO";
}
