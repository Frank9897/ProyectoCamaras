using System.Net;
using System.Net.Sockets;
using System.Text;
using CameraInspector.Core.Interfaces;
using CameraInspector.Core.Models;

namespace CameraInspector.Network.Providers.Vivotek;

/// <summary>
/// Descubrimiento propietario de VIVOTEK compatible con el mecanismo de broadcast utilizado por Shepherd.
/// La implementación soporta tanto redes normales como cámaras en APIPA 169.254.x.x.
/// </summary>
public sealed class VivotekDiscoveryService : IVivotekDiscoveryService
{
    // VIVOTEK documenta UDP 5678 como puerto principal del broadcast de discovery.
    private const int ShepherdDiscoveryPort = 5678;

    // Algunas generaciones/implementaciones responden mediante el flujo históricamente observado con UDP 10000.
    // Lo conservamos como compatibilidad para no perder cámaras de firmware antiguo.
    private const int LegacyDiscoveryPort = 10000;

    private readonly TimeSpan _discoveryTimeout = TimeSpan.FromSeconds(3);

    public async Task<IReadOnlyList<DiscoveredDevice>> DiscoverAsync(
        NetworkInterfaceInfo networkInterface,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(networkInterface);

        var results = new Dictionary<string, DiscoveredDevice>(StringComparer.OrdinalIgnoreCase);
        var bindAddress = networkInterface.IpAddress;
        var broadcastAddress = CalculateBroadcastAddress(networkInterface.IpAddress, networkInterface.SubnetMask);

        // APIPA necesita explícitamente el broadcast de 169.254.0.0/16.
        var isApipa = bindAddress.AddressFamily == AddressFamily.InterNetwork &&
                      IsApipa(bindAddress);

        var broadcasts = new List<IPAddress>
        {
            broadcastAddress,
            IPAddress.Broadcast
        };

        if (isApipa)
            broadcasts.Add(IPAddress.Parse("169.254.255.255"));

        // Eliminamos duplicados de broadcast antes de enviar.
        broadcasts = broadcasts
            .Distinct()
            .ToList();

        // Intentamos primero el puerto local documentado por VIVOTEK/Shepherd.
        using var socket = await CreateBoundSocketAsync(bindAddress, ShepherdDiscoveryPort, cancellationToken);
        socket.EnableBroadcast = true;

        var probe = BuildProbe();

        // VIVOTEK documenta 5678 para el broadcast de Shepherd; 10000 queda como fallback de compatibilidad.
        foreach (var destinationPort in new[] { ShepherdDiscoveryPort, LegacyDiscoveryPort })
        {
            foreach (var target in broadcasts)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    await socket.SendAsync(
                        probe,
                        probe.Length,
                        new IPEndPoint(target, destinationPort));
                }
                catch (SocketException)
                {
                    // Un broadcast concreto puede estar bloqueado por la topología/NIC; probamos el siguiente.
                }
            }
        }

        var deadline = DateTimeOffset.UtcNow + _discoveryTimeout;
        while (!cancellationToken.IsCancellationRequested)
        {
            var remaining = deadline - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero)
                break;

            UdpReceiveResult packet;
            try
            {
                var receiveTask = socket.ReceiveAsync(cancellationToken).AsTask();
                var timeoutTask = Task.Delay(remaining, cancellationToken);
                var completed = await Task.WhenAny(receiveTask, timeoutTask);

                if (completed != receiveTask)
                    break;

                packet = await receiveTask;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (SocketException)
            {
                continue;
            }

            // No restringimos la respuesta a un puerto origen concreto: el discovery puede devolver desde
            // implementaciones/firmwares diferentes. La evidencia fuerte es el formato VIVOTEK del payload.
            if (!LooksLikeVivotekResponse(packet.Buffer))
                continue;

            var device = CreateDiscoveredDevice(packet.RemoteEndPoint.Address, packet.Buffer);
            var key = device.IpAddress;

            if (results.TryGetValue(key, out var existing))
            {
                MergeEvidence(existing, device);
                continue;
            }

            results[key] = device;
        }

        return results.Values.ToList();
    }

    private static async Task<UdpClient> CreateBoundSocketAsync(
        IPAddress bindAddress,
        int preferredPort,
        CancellationToken cancellationToken)
    {
        try
        {
            return new UdpClient(new IPEndPoint(bindAddress, preferredPort));
        }
        catch (SocketException)
        {
            // Si otro proceso ya usa 5678, usar un puerto efímero mantiene funcional el discovery de la mayoría de firmwares.
            var fallback = new UdpClient(new IPEndPoint(bindAddress, 0));
            await Task.CompletedTask;
            cancellationToken.ThrowIfCancellationRequested();
            return fallback;
        }
    }

    private static byte[] BuildProbe()
    {
        var session = Guid.NewGuid().ToByteArray();

        // Formato mínimo públicamente observado: 01 + 3 bytes de sesión + 03.
        return new[]
        {
            (byte)0x01,
            session[0],
            session[1],
            session[2],
            (byte)0x03
        };
    }

    private static IPAddress CalculateBroadcastAddress(IPAddress ipAddress, IPAddress subnetMask)
    {
        var ipBytes = ipAddress.GetAddressBytes();
        var maskBytes = subnetMask.GetAddressBytes();

        if (ipBytes.Length != 4 || maskBytes.Length != 4)
            return IPAddress.Broadcast;

        var broadcastBytes = new byte[4];
        for (var index = 0; index < 4; index++)
            broadcastBytes[index] = (byte)(ipBytes[index] | ~maskBytes[index]);

        return new IPAddress(broadcastBytes);
    }

    private static bool IsApipa(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return bytes.Length == 4 && bytes[0] == 169 && bytes[1] == 254;
    }

    private static bool LooksLikeVivotekResponse(byte[] payload)
    {
        if (payload.Length < 8)
            return false;

        // Las respuestas VIVOTEK conocidas comienzan con 0x02 y contienen identificadores/strings de producto.
        if (payload[0] == 0x02)
            return true;

        // Compatibilidad adicional: aceptar una carga que contenga un prefijo ASCII VIVOTEK.
        var text = Encoding.ASCII.GetString(payload);
        return text.Contains("VIVOTEK", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("VIVOTEK", StringComparison.OrdinalIgnoreCase);
    }

    private static DiscoveredDevice CreateDiscoveredDevice(IPAddress address, byte[] payload)
    {
        var model = ExtractModel(payload);
        var mac = ExtractVivotekMac(payload);

        return new DiscoveredDevice
        {
            IpAddress = address.ToString(),
            MacAddress = mac,
            Manufacturer = "VIVOTEK",
            Model = model,
            AssignedProviderName = "VIVOTEK",
            Status = DeviceStatus.Online,
            HttpSupported = true,
            OnvifSupported = false
        };
    }

    private static string? ExtractModel(byte[] payload)
    {
        // Extrae una secuencia ASCII imprimible razonable. Evitamos exponer bytes binarios como modelo.
        var candidates = new List<string>();
        var current = new StringBuilder();

        foreach (var value in payload)
        {
            var printable = value is >= 0x20 and <= 0x7E;
            if (printable)
            {
                current.Append((char)value);
                continue;
            }

            if (current.Length >= 5)
                candidates.Add(current.ToString());

            current.Clear();
        }

        if (current.Length >= 5)
            candidates.Add(current.ToString());

        // Priorizamos patrones de modelo de VIVOTEK (por ejemplo IB9360, FD8166, IT9388, etc.).
        return candidates
            .Where(item => item.Length <= 48)
            .OrderByDescending(item => item.Any(char.IsLetter) && item.Any(char.IsDigit))
            .ThenBy(item => item.Length)
            .FirstOrDefault();
    }

    private static string? ExtractVivotekMac(byte[] payload)
    {
        // La documentación de VIVOTEK indica que sus MAC suelen comenzar por 00-02-D1.
        // Buscamos esa firma dentro de la respuesta binaria del discovery.
        for (var index = 0; index <= payload.Length - 6; index++)
        {
            if (payload[index] != 0x00 || payload[index + 1] != 0x02 || payload[index + 2] != 0xD1)
                continue;

            return string.Join(
                ":",
                payload.Skip(index).Take(6).Select(value => value.ToString("X2")));
        }

        return null;
    }

    private static void MergeEvidence(DiscoveredDevice target, DiscoveredDevice source)
    {
        if (string.IsNullOrWhiteSpace(target.MacAddress))
            target.MacAddress = source.MacAddress;

        if (string.IsNullOrWhiteSpace(target.Model))
            target.Model = source.Model;

        target.Manufacturer = "VIVOTEK";
        target.AssignedProviderName = "VIVOTEK";
        target.Status = DeviceStatus.Online;
        target.HttpSupported |= source.HttpSupported;
    }
}
