using CameraInspector.Core.Models;

namespace CameraInspector.Core.Interfaces;

/// <summary>
/// Una unidad de detección del pipeline de la Capa 4. Cada implementación evalúa
/// UNA señal (OUI de la MAC, banner HTTP, respuesta ONVIF, etc.) y devuelve null si
/// no pudo aportar nada — nunca lanza para "no encontré nada", eso es un resultado válido.
/// Agregar un fabricante o técnica de detección nueva = agregar una clase nueva acá,
/// sin tocar el resolver ni ningún otro detector existente.
/// </summary>
public interface IManufacturerDetector
{
    /// <summary>Nombre corto para logging/depuración (ej. "OuiMac", "HttpBanner", "OnvifProbe").</summary>
    string Name { get; }

    Task<ManufacturerDetectionResult?> TryDetectAsync(
        DiscoveredDevice device,
        CancellationToken cancellationToken = default);
}
