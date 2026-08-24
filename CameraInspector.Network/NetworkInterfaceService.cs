using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Text.Json;
using CameraInspector.Core.Interfaces;
using CameraInspector.Core.Models;

namespace CameraInspector.Network;

/// <summary>
/// Implementación real sobre System.Net.NetworkInformation.
/// Además de las interfaces físicas locales, agrega las subredes IPv4 enrutable
/// que Windows conoce en su tabla de rutas. Esto permite seleccionar redes remotas
/// del laboratorio que estén disponibles mediante routing.
/// </summary>
public sealed class NetworkInterfaceService : INetworkInterfaceService
{
    public IReadOnlyList<NetworkInterfaceInfo> GetActiveInterfaces()
    {
        var result = new List<NetworkInterfaceInfo>();

        // Primero agregamos las interfaces locales reales del equipo.
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up)
                continue;

            if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                continue;

            var ipProps = nic.GetIPProperties();
            var unicast = ipProps.UnicastAddresses
                .FirstOrDefault(a => a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);

            if (unicast is null)
                continue;

            // prefixLength indica cuántos bits de la dirección representan la red.
            var prefixLength = unicast.PrefixLength;

            // isWireless identifica adaptadores Wi-Fi para conservar la información visual de la interfaz.
            var isWireless = nic.NetworkInterfaceType is NetworkInterfaceType.Wireless80211;

            result.Add(new NetworkInterfaceInfo
            {
                Name = nic.Name,
                Description = nic.Description,
                IpAddress = unicast.Address,
                SubnetMask = unicast.IPv4Mask ?? PrefixToMask(prefixLength),
                CidrPrefixLength = prefixLength,
                IsWireless = isWireless,
                IsUp = true
            });
        }

        // Las rutas remotas se agregan como objetivos virtuales seleccionables.
        // No modificamos las interfaces reales; simplemente ampliamos las opciones de escaneo.
        foreach (var route in GetRoutedSubnets())
        {
            // exists evita duplicar una subred que ya corresponde a una interfaz local.
            var exists = result.Any(item =>
                item.IpAddress.Equals(route.NetworkAddress)
                && item.CidrPrefixLength == route.PrefixLength);

            if (exists)
                continue;

            result.Add(new NetworkInterfaceInfo
            {
                Name = $"Ruta IPv4 · {route.InterfaceAlias}",
                Description = string.IsNullOrWhiteSpace(route.NextHop)
                    ? "Subred IPv4 enrutable detectada por Windows"
                    : $"Subred IPv4 enrutable vía {route.NextHop}",
                IpAddress = route.NetworkAddress,
                SubnetMask = PrefixToMask(route.PrefixLength),
                CidrPrefixLength = route.PrefixLength,
                IsWireless = false,
                IsUp = true
            });
        }

        return result;
    }

    /// <summary>
    /// Obtiene rutas IPv4 unicast desde Windows mediante Get-NetRoute.
    /// Solo conservamos prefijos de red razonables para un escaneo de laboratorio (/8 a /30).
    /// </summary>
    private static IReadOnlyList<WindowsRoute> GetRoutedSubnets()
    {
        var result = new List<WindowsRoute>();

        try
        {
            // El comando es fijo y no incorpora datos del usuario, por lo que no existe concatenación insegura.
            const string command =
                "Get-NetRoute -AddressFamily IPv4 | " +
                "Where-Object { $_.DestinationPrefix -ne '0.0.0.0/0' } | " +
                "Select-Object DestinationPrefix,InterfaceAlias,NextHop | " +
                "ConvertTo-Json -Compress";

            // startInfo configura PowerShell sin perfil para obtener una salida predecible y de solo lectura.
            var startInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -NonInteractive -Command \"{command}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process is null)
                return result;

            // json contiene únicamente las rutas seleccionadas por Get-NetRoute.
            var json = process.StandardOutput.ReadToEnd();
            process.WaitForExit(1500);

            if (string.IsNullOrWhiteSpace(json))
                return result;

            // ConvertTo-Json devuelve un objeto cuando solo existe una ruta y un arreglo cuando existen varias.
            if (json.TrimStart().StartsWith("[", StringComparison.Ordinal))
            {
                var routes = JsonSerializer.Deserialize<List<WindowsRouteDto>>(json);
                if (routes is not null)
                    AddRoutes(result, routes);
            }
            else
            {
                var route = JsonSerializer.Deserialize<WindowsRouteDto>(json);
                if (route is not null)
                    AddRoutes(result, new[] { route });
            }
        }
        catch
        {
            // La tabla de rutas es una mejora opcional: si PowerShell no está disponible,
            // conservamos las interfaces locales y no interrumpimos el descubrimiento.
        }

        return result;
    }

    private static void AddRoutes(
        ICollection<WindowsRoute> destination,
        IEnumerable<WindowsRouteDto> routes)
    {
        foreach (var route in routes)
        {
            if (!TryParseNetwork(route.DestinationPrefix, out var networkAddress, out var prefixLength))
                continue;

            // Se excluyen rutas demasiado pequeñas o especiales; /32 representa hosts concretos,
            // no una subred que queramos barrer como objetivo general.
            if (prefixLength < 8 || prefixLength > 30)
                continue;

            // Se excluyen rangos loopback, multicast y link-local porque no son redes de cámaras gestionables.
            var firstOctet = networkAddress.GetAddressBytes()[0];
            if (firstOctet == 127 || firstOctet == 169 || firstOctet >= 224)
                continue;

            destination.Add(new WindowsRoute(
                networkAddress,
                prefixLength,
                route.InterfaceAlias ?? string.Empty,
                route.NextHop ?? string.Empty));
        }
    }

    private static bool TryParseNetwork(
        string? destinationPrefix,
        out IPAddress networkAddress,
        out int prefixLength)
    {
        networkAddress = IPAddress.None;
        prefixLength = 0;

        if (string.IsNullOrWhiteSpace(destinationPrefix))
            return false;

        var parts = destinationPrefix.Split('/', StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || !IPAddress.TryParse(parts[0], out var address))
            return false;

        if (!int.TryParse(parts[1], out prefixLength) || prefixLength is < 0 or > 32)
            return false;

        // networkAddress normaliza el prefijo para garantizar que el escaneo comience en el límite de red.
        networkAddress = GetNetworkAddress(address, PrefixToMask(prefixLength));
        return true;
    }

    private static IPAddress GetNetworkAddress(IPAddress address, IPAddress mask)
    {
        var addressBytes = address.GetAddressBytes();
        var maskBytes = mask.GetAddressBytes();
        var networkBytes = new byte[4];

        for (var index = 0; index < 4; index++)
            networkBytes[index] = (byte)(addressBytes[index] & maskBytes[index]);

        return new IPAddress(networkBytes);
    }

    private static IPAddress PrefixToMask(int prefixLength)
    {
        // Fallback por si IPv4Mask viene null y para convertir prefijos de rutas a una máscara IPv4.
        if (prefixLength == 0)
            return IPAddress.Any;

        uint mask = 0xFFFFFFFF << (32 - prefixLength);
        var bytes = BitConverter.GetBytes(mask);
        if (BitConverter.IsLittleEndian)
            Array.Reverse(bytes);

        return new IPAddress(bytes);
    }

    private sealed record WindowsRoute(
        IPAddress NetworkAddress,
        int PrefixLength,
        string InterfaceAlias,
        string NextHop);

    private sealed class WindowsRouteDto
    {
        public string? DestinationPrefix { get; set; }
        public string? InterfaceAlias { get; set; }
        public string? NextHop { get; set; }
    }
}
