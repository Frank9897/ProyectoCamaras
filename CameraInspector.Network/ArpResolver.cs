using System.Net;
using System.Runtime.InteropServices;
using CameraInspector.Core.Interfaces;

namespace CameraInspector.Network;

/// <summary>
/// Lee la tabla ARP del sistema operativo vía iphlpapi.dll (GetIpNetTable).
/// Esto solo funciona porque previamente hicimos ping o algún TCP connect a esas IPs
/// (el propio SO puebla su caché ARP como efecto secundario del tráfico) — por eso
/// en el pipeline el orden es siempre: ping sweep primero, resolución ARP después.
/// </summary>
public sealed class ArpResolver : IArpResolver
{
    public IReadOnlyDictionary<IPAddress, string> GetArpTable()
    {
        var result = new Dictionary<IPAddress, string>();

        int bufferSize = 0;
        GetIpNetTable(IntPtr.Zero, ref bufferSize, false);

        var buffer = Marshal.AllocHGlobal(bufferSize);
        try
        {
            int ret = GetIpNetTable(buffer, ref bufferSize, false);
            if (ret != 0)
                return result; // tabla no disponible: se degrada sin romper el flujo

            int entryCount = Marshal.ReadInt32(buffer);
            var rowPtr = IntPtr.Add(buffer, 4);
            int rowSize = Marshal.SizeOf<MIB_IPNETROW>();

            for (int i = 0; i < entryCount; i++)
            {
                var row = Marshal.PtrToStructure<MIB_IPNETROW>(
                    IntPtr.Add(rowPtr, i * rowSize));

                // Tipo 3 = "dynamic", tipo 4 = "static". Ignoramos "invalid"/"other".
                if (row.Type is not (3 or 4))
                    continue;

                var ip = new IPAddress(BitConverter.GetBytes(row.Addr));
                var mac = string.Join(":", new[]
                {
                    row.PhysAddr0, row.PhysAddr1, row.PhysAddr2,
                    row.PhysAddr3, row.PhysAddr4, row.PhysAddr5
                }.Select(b => b.ToString("X2")));

                if (mac != "00:00:00:00:00:00")
                    result[ip] = mac;
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        return result;
    }

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern int GetIpNetTable(IntPtr pIpNetTable, ref int pdwSize, bool bOrder);

    [StructLayout(LayoutKind.Sequential)]
    private struct MIB_IPNETROW
    {
        public int Index;
        public int PhysAddrLen;
        public byte PhysAddr0, PhysAddr1, PhysAddr2, PhysAddr3, PhysAddr4, PhysAddr5, PhysAddr6, PhysAddr7;
        public uint Addr;
        public int Type;
    }
}
