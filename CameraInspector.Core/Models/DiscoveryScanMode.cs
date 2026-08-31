namespace CameraInspector.Core.Models;

/// <summary>
/// Define el alcance con el que Camera Inspector realiza un descubrimiento de cámaras.
/// </summary>
public enum DiscoveryScanMode
{
    /// <summary>
    /// Descubrimiento orientado a una cámara conectada directamente a la interfaz seleccionada.
    /// No genera un ping sweep de toda la subred; utiliza los mecanismos de discovery disponibles.
    /// </summary>
    DirectCamera,

    /// <summary>
    /// Escaneo de la subred asociada a la interfaz seleccionada.
    /// </summary>
    NetworkSubnet,

    /// <summary>
    /// Escaneo de todas las interfaces de red activas que sean elegibles.
    /// </summary>
    FullNetwork
}
