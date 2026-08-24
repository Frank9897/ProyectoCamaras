using System.Net;
using CameraInspector.Core.Interfaces;
using CameraInspector.Core.Models;

namespace CameraInspector.Network;

/// <summary>
/// Calcula el rango de IPs host de una subred IPv4. Para redes /24 esto es trivial (254 hosts),
/// pero lo dejamos genérico porque en sitios reales de CCTV aparecen /23 o /22 con NVRs grandes.
/// </summary>
public sealed class SubnetCalculator : ISubnetCalculator
{
    public IEnumerable<IPAddress> GetHostAddresses(NetworkInterfaceInfo networkInterface)
    {
        var ipBytes = networkInterface.IpAddress.GetAddressBytes();
        var maskBytes = networkInterface.SubnetMask.GetAddressBytes();

        var networkBytes = new byte[4];
        var broadcastBytes = new byte[4];
        for (int i = 0; i < 4; i++)
        {
            networkBytes[i] = (byte)(ipBytes[i] & maskBytes[i]);
            broadcastBytes[i] = (byte)(networkBytes[i] | ~maskBytes[i]);
        }

        uint networkAddr = ToUInt32(networkBytes);
        uint broadcastAddr = ToUInt32(broadcastBytes);

        // Límite de seguridad: si por error se detecta una subred enorme (ej. /8),
        // no queremos generar millones de IPs y colgar el escaneo.
        const uint maxHosts = 4096;
        if (broadcastAddr - networkAddr > maxHosts)
        {
            throw new InvalidOperationException(
                $"La subred detectada ({networkInterface}) es demasiado grande para escanear " +
                $"de forma segura (> {maxHosts} hosts). Verificá la máscara de red.");
        }

        for (uint addr = networkAddr + 1; addr < broadcastAddr; addr++)
        {
            yield return FromUInt32(addr);
        }
    }

    private static uint ToUInt32(byte[] bytes) =>
        ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];

    private static IPAddress FromUInt32(uint value)
    {
        var bytes = new byte[]
        {
            (byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value
        };
        return new IPAddress(bytes);
    }
}
