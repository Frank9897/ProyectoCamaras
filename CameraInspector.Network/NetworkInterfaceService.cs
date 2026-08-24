using System.Net;
using System.Net.NetworkInformation;
using CameraInspector.Core.Interfaces;
using CameraInspector.Core.Models;

namespace CameraInspector.Network;

/// <summary>
/// Implementación real sobre System.Net.NetworkInformation.
/// Filtra loopback, túneles y adaptadores caídos: solo interesan Ethernet/Wi-Fi activos con IPv4.
/// </summary>
public sealed class NetworkInterfaceService : INetworkInterfaceService
{
    public IReadOnlyList<NetworkInterfaceInfo> GetActiveInterfaces()
    {
        var result = new List<NetworkInterfaceInfo>();

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

            var prefixLength = unicast.PrefixLength;
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

        return result;
    }

    private static IPAddress PrefixToMask(int prefixLength)
    {
        // Fallback por si IPv4Mask viene null en algunos adaptadores virtuales.
        uint mask = prefixLength == 0 ? 0 : 0xFFFFFFFF << (32 - prefixLength);
        var bytes = BitConverter.GetBytes(mask);
        if (BitConverter.IsLittleEndian) Array.Reverse(bytes);
        return new IPAddress(bytes);
    }
}
