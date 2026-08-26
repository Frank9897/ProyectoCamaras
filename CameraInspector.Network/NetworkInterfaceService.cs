using System.Net;
using System.Net.NetworkInformation;
using CameraInspector.Core.Interfaces;
using CameraInspector.Core.Models;

namespace CameraInspector.Network;

/// <summary>
/// Enumerador real de las interfaces IPv4 activas del equipo.
/// Cada entrada representa un puerto/adaptador de red, no una ruta virtual.
/// </summary>
public sealed class NetworkInterfaceService : INetworkInterfaceService
{
    public IReadOnlyList<NetworkInterfaceInfo> GetActiveInterfaces()
    {
        var result = new List<NetworkInterfaceInfo>();

        // Enumeramos todos los adaptadores instalados y dejamos fuera los que no están operativos.
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up)
                continue;

            // Loopback y túneles no representan un puerto útil para descubrir cámaras físicas.
            if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                continue;

            var ipProperties = nic.GetIPProperties();
            var ipv4 = ipProperties.UnicastAddresses
                .FirstOrDefault(address =>
                    address.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
                    && address.IPv4Mask is not null);

            if (ipv4 is null)
                continue;

            // prefixLength permite calcular automáticamente la red que será explorada.
            var prefixLength = ipv4.PrefixLength;
            var subnetMask = ipv4.IPv4Mask ?? PrefixToMask(prefixLength);
            var networkAddress = GetNetworkAddress(ipv4.Address, subnetMask);

            // gateway toma el primer gateway IPv4 válido configurado para esta interfaz.
            var gateway = ipProperties.GatewayAddresses
                .Select(item => item.Address)
                .FirstOrDefault(address =>
                    address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);

            // usesDhcp informa si Windows tiene DHCP habilitado para este adaptador.
            var usesDhcp = ipv4.PrefixOrigin == PrefixOrigin.Dhcp
                           || ipv4.AddressPreferredLifetime != TimeSpan.Zero
                              && ipProperties.DhcpServerAddresses.Any();

            // macAddress mantiene la identidad física del puerto para mostrarla al técnico.
            var macAddress = nic.GetPhysicalAddress().ToString();
            if (string.IsNullOrWhiteSpace(macAddress))
                macAddress = null;

            result.Add(new NetworkInterfaceInfo
            {
                Name = nic.Name,
                Description = nic.Description,
                InterfaceId = nic.Id,
                MacAddress = macAddress,
                IpAddress = ipv4.Address,
                SubnetMask = subnetMask,
                CidrPrefixLength = prefixLength,
                NetworkAddress = networkAddress,
                DefaultGateway = gateway,
                UsesDhcp = usesDhcp,
                IsWireless = nic.NetworkInterfaceType is NetworkInterfaceType.Wireless80211,
                IsUp = true
            });
        }

        // Ordenamos por nombre para que el selector no cambie de posición arbitrariamente entre ejecuciones.
        return result
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IPAddress GetNetworkAddress(IPAddress address, IPAddress mask)
    {
        // addressBytes contiene la IPv4 del adaptador que estamos inspeccionando.
        var addressBytes = address.GetAddressBytes();
        // maskBytes contiene la máscara que define qué bits pertenecen a la red.
        var maskBytes = mask.GetAddressBytes();
        // networkBytes almacenará el resultado binario de IP AND máscara.
        var networkBytes = new byte[4];

        for (var index = 0; index < networkBytes.Length; index++)
            networkBytes[index] = (byte)(addressBytes[index] & maskBytes[index]);

        return new IPAddress(networkBytes);
    }

    private static IPAddress PrefixToMask(int prefixLength)
    {
        // Un prefijo 0 significa que ningún bit pertenece a la red.
        if (prefixLength == 0)
            return IPAddress.Any;

        // mask construye los bits altos correspondientes al prefijo CIDR.
        uint mask = 0xFFFFFFFF << (32 - prefixLength);
        var bytes = BitConverter.GetBytes(mask);

        // Windows utiliza little-endian, mientras que IPv4 se representa en orden de red.
        if (BitConverter.IsLittleEndian)
            Array.Reverse(bytes);

        return new IPAddress(bytes);
    }
}
