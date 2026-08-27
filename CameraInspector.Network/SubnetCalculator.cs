using System.Net;
using CameraInspector.Core.Interfaces;
using CameraInspector.Core.Models;

namespace CameraInspector.Network;

/// <summary>
/// Calcula el rango de IPs host de una subred IPv4. Las subredes normales se limitan
/// para evitar barridos gigantes; las interfaces link-local se excluyen del ping sweep
/// porque su /16 no representa una LAN que debamos recorrer completa.
/// </summary>
public sealed class SubnetCalculator : ISubnetCalculator
{
    public IEnumerable<IPAddress> GetHostAddresses(NetworkInterfaceInfo networkInterface)
    {
        // ipBytes contiene los cuatro octetos de la IPv4 configurada en el adaptador.
        var ipBytes = networkInterface.IpAddress.GetAddressBytes();
        // maskBytes contiene los cuatro octetos de la máscara IPv4 configurada.
        var maskBytes = networkInterface.SubnetMask.GetAddressBytes();

        // Una dirección 169.254.x.x pertenece al rango IPv4 link-local/autoconfigurado.
        // No hacemos un barrido /16 porque generaría decenas de miles de candidatos y no es
        // necesario para descubrir cámaras mediante WS-Discovery en la misma interfaz.
        if (ipBytes.Length == 4 && ipBytes[0] == 169 && ipBytes[1] == 254)
            yield break;

        var networkBytes = new byte[4];
        var broadcastBytes = new byte[4];
        for (int i = 0; i < 4; i++)
        {
            // networkBytes[i] obtiene la parte de red aplicando AND entre IP y máscara.
            networkBytes[i] = (byte)(ipBytes[i] & maskBytes[i]);
            // broadcastBytes[i] obtiene la dirección de broadcast usando la máscara invertida.
            broadcastBytes[i] = (byte)(networkBytes[i] | ~maskBytes[i]);
        }

        // networkAddr es la red expresada como entero para poder recorrerla de forma eficiente.
        uint networkAddr = ToUInt32(networkBytes);
        // broadcastAddr es el límite superior de la subred expresado como entero.
        uint broadcastAddr = ToUInt32(broadcastBytes);

        // maxHosts limita el número de hosts que el ping sweep puede generar en una ejecución.
        const uint maxHosts = 4096;
        if (broadcastAddr - networkAddr > maxHosts)
        {
            throw new InvalidOperationException(
                $"La subred detectada ({networkInterface}) es demasiado grande para escanear " +
                $"de forma segura (> {maxHosts} hosts). Verificá la máscara de red o usá descubrimiento multicast.");
        }

        // addr comienza en el primer host y termina antes de broadcast, excluyendo red y broadcast.
        for (uint addr = networkAddr + 1; addr < broadcastAddr; addr++)
        {
            yield return FromUInt32(addr);
        }
    }

    private static uint ToUInt32(byte[] bytes) =>
        ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];

    private static IPAddress FromUInt32(uint value)
    {
        // bytes descompone el entero IPv4 nuevamente en sus cuatro octetos.
        var bytes = new byte[]
        {
            (byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value
        };
        return new IPAddress(bytes);
    }
}
