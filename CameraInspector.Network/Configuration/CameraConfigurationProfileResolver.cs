using CameraInspector.Core.Models;

namespace CameraInspector.Network.Configuration;

/// <summary>
/// Selecciona el flujo de configuración más apropiado según fabricante.
/// Cuando no existe un fabricante conocido se utiliza un perfil genérico basado en ONVIF/HTTP.
/// </summary>
public static class CameraConfigurationProfileResolver
{
    private static readonly CameraConfigurationProfile Generic = new(
        "GENÉRICO",
        "Perfil genérico ONVIF / HTTP",
        "Discovery de la aplicación",
        "Configuración estándar con ONVIF cuando está disponible; web como respaldo",
        "ONVIF / HTTP",
        true, true, true, true, true, true, true, true, true, false,
        "No se identificó un fabricante con un flujo propietario conocido. Se muestran únicamente operaciones compatibles con las capacidades detectadas y se mantiene un respaldo web.",
        [
            "Leer capacidades antes de modificar",
            "Validar IPv4, prefijo, gateway y DNS",
            "Aplicar cambios con confirmación",
            "Comprobar nuevamente la cámara después de una modificación"
        ]);

    private static readonly CameraConfigurationProfile Vivotek = new(
        "VIVOTEK",
        "Perfil VIVOTEK / estilo Wizard",
        "VIVOTEK Installation Wizard 2 / Shepherd",
        "Descubrimiento propietario + configuración web/CGI; ONVIF como complemento",
        "VIVOTEK CGI / HTTP + ONVIF",
        true, true, true, true, true, true, true, true, true, true,
        "VIVOTEK dispone de herramientas de descubrimiento y asignación de IP propias. La interfaz prioriza el fabricante y deja ONVIF como respaldo cuando el modelo lo anuncia.",
        [
            "Confirmar MAC y modelo antes de cambiar IP",
            "Priorizar el flujo VIVOTEK para cámaras antiguas o sin ONVIF",
            "Abrir la interfaz web/CGI para parámetros propietarios",
            "Volver a descubrir la cámara tras cambiar su IP"
        ]);

    private static readonly CameraConfigurationProfile Hikvision = new(
        "HIKVISION",
        "Perfil Hikvision / estilo SADP",
        "Hikvision SADP",
        "Activación + red + gestión del dispositivo",
        "SADP / HTTP(S) / ONVIF",
        true, true, true, true, true, true, true, true, true, true,
        "Hikvision usa SADP para descubrir, activar y modificar parámetros de red. El flujo de configuración diferencia activación de administración posterior.",
        [
            "Comprobar si el dispositivo está activo",
            "Configurar IP, máscara, gateway y DNS",
            "Validar credenciales antes de operaciones administrativas",
            "Reiniciar o volver a descubrir después de cambios de red"
        ]);

    private static readonly CameraConfigurationProfile Dahua = new(
        "DAHUA",
        "Perfil Dahua / estilo ConfigTool",
        "Dahua ConfigTool",
        "Descubrimiento propietario + configuración de red del dispositivo",
        "DHIP / HTTP(S) / ONVIF",
        true, true, true, true, true, true, true, true, true, true,
        "Dahua utiliza ConfigTool para localizar dispositivos y modificar su direccionamiento. El panel conserva un respaldo ONVIF cuando existe.",
        [
            "Seleccionar el dispositivo por MAC/serie",
            "Configurar IP, máscara, gateway y DNS",
            "Validar el resultado y volver a buscar",
            "Usar web/ONVIF para funciones específicas del modelo"
        ]);

    private static readonly CameraConfigurationProfile Axis = new(
        "AXIS",
        "Perfil AXIS / IP Utility + Device Manager",
        "AXIS IP Utility / AXIS Device Manager",
        "Gestión del dispositivo + parámetros + estado",
        "HTTP(S) / ONVIF / AXIS",
        true, true, true, true, true, true, true, true, true, false,
        "AXIS separa la asignación de IP de la administración avanzada. Se priorizan IP Utility/Device Manager como referencia conceptual y se evita asumir un factory reset genérico.",
        [
            "Comprobar nombre de host y estado de conexión",
            "Configurar IP, máscara, gateway y DNS",
            "Configurar NTP cuando esté disponible",
            "Usar la administración web/AXIS para parámetros avanzados"
        ]);

    private static readonly CameraConfigurationProfile Hanwha = new(
        "HANWHA",
        "Perfil Hanwha / Wisenet Device Manager",
        "Wisenet Device Manager",
        "Asignación de IP + credenciales + configuración del dispositivo",
        "Wisenet / HTTP(S) / ONVIF",
        true, true, true, true, true, true, true, true, true, true,
        "Wisenet Device Manager permite escanear la LAN, gestionar credenciales, asignar IP y configurar fecha/NTP y otros parámetros del dispositivo.",
        [
            "Comprobar si el dispositivo requiere activación/contraseña",
            "Asignar IP de forma controlada",
            "Configurar NTP y zona horaria",
            "Abrir la configuración de dispositivo para parámetros avanzados"
        ]);

    private static readonly CameraConfigurationProfile Uniview = new(
        "UNIVIEW",
        "Perfil Uniview / estilo EZTools",
        "EZTools",
        "Modificar dirección de red desde herramienta propietaria",
        "EZTools / HTTP(S) / ONVIF",
        true, true, true, true, true, true, true, true, true, true,
        "Uniview documenta EZTools para modificar IP, máscara y gateway, con credenciales del dispositivo cuando corresponde.",
        [
            "Seleccionar cámara y validar identidad",
            "Modificar IP, máscara y gateway",
            "Confirmar credenciales antes de aplicar",
            "Actualizar la lista después del cambio"
        ]);

    private static readonly CameraConfigurationProfile Reolink = new(
        "REOLINK",
        "Perfil Reolink / estilo Client",
        "Reolink Client / App",
        "Network General + puertos + NTP/servicios",
        "HTTP(S) / RTSP / ONVIF",
        true, true, true, true, true, true, true, true, true, false,
        "Reolink organiza la red en Network General y separa puertos, NTP y servicios avanzados. No se debe asumir que todas las cámaras tienen exactamente el mismo menú.",
        [
            "Configurar DHCP o Static",
            "Revisar gateway y DNS",
            "Revisar puertos HTTP/HTTPS/RTSP/ONVIF cuando estén disponibles",
            "Comprobar NTP y acceso remoto si corresponde"
        ]);

    private static readonly CameraConfigurationProfile Mobotix = new(
        "MOBOTIX",
        "Perfil MOBOTIX / estilo MxManagementCenter",
        "MxManagementCenter",
        "Configuración de red avanzada y opciones adicionales",
        "HTTP(S) / ONVIF + MOBOTIX",
        true, true, true, true, true, true, true, true, true, false,
        "MOBOTIX puede mantener configuraciones de red adicionales y su gestión de red contempla opciones más avanzadas que un panel ONVIF mínimo.",
        [
            "Comprobar DHCP/static y gateway",
            "Revisar DNS y NTP",
            "Considerar IP adicional cuando el modelo lo soporte",
            "Aplicar cambios y verificar recuperación"
        ]);

    public static CameraConfigurationProfile Resolve(DiscoveredDevice device)
    {
        var manufacturer = device.Manufacturer ?? string.Empty;
        var model = device.Model ?? string.Empty;
        var value = string.Concat(manufacturer, " ", model);

        if (value.Contains("VIVOTEK", StringComparison.OrdinalIgnoreCase)) return Vivotek;
        if (value.Contains("HIKVISION", StringComparison.OrdinalIgnoreCase) || value.Contains("HIK VISION", StringComparison.OrdinalIgnoreCase)) return Hikvision;
        if (value.Contains("DAHUA", StringComparison.OrdinalIgnoreCase)) return Dahua;
        if (value.Contains("AXIS", StringComparison.OrdinalIgnoreCase)) return Axis;
        if (value.Contains("HANWHA", StringComparison.OrdinalIgnoreCase) || value.Contains("WISENET", StringComparison.OrdinalIgnoreCase)) return Hanwha;
        if (value.Contains("UNIVIEW", StringComparison.OrdinalIgnoreCase) || value.Contains("UNV", StringComparison.OrdinalIgnoreCase)) return Uniview;
        if (value.Contains("REOLINK", StringComparison.OrdinalIgnoreCase)) return Reolink;
        if (value.Contains("MOBOTIX", StringComparison.OrdinalIgnoreCase)) return Mobotix;

        return Generic;
    }
}
