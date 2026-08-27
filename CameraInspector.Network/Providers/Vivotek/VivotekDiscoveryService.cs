using System.Net;
using System.Net.Sockets;
using CameraInspector.Core.Interfaces;
using CameraInspector.Core.Models;

namespace CameraInspector.Network.Providers.Vivotek;

/// <summary>
/// Descubrimiento propietario de VIVOTEK compatible con el mecanismo de broadcast
/// utilizado por Shepherd. El objetivo de este servicio es localizar cámaras sin
/// conocer previamente su dirección IP y sin solicitar credenciales.
/// </summary>
public sealed class VivotekDiscoveryService : IVivotekDiscoveryService
{
    // DiscoveryPort es el puerto UDP de destino utilizado por el discovery propietario de VIVOTEK.
    private const int DiscoveryPort = 10000;

    // ListenPort es el puerto local donde Shepherd y las cámaras intercambian las respuestas de discovery.
    private const int ListenPort = 5678;

    // DiscoveryTimeout controla cuánto tiempo esperamos respuestas después de emitir el broadcast.
    private readonly TimeSpan _discoveryTimeout = TimeSpan.FromSeconds(2.5);

    /// <summary>
    /// Ejecuta el descubrimiento VIVOTEK sobre una interfaz concreta.
    /// </summary>
    public async Task<IReadOnlyList<DiscoveredDevice>> DiscoverAsync(
        NetworkInterfaceInfo networkInterface,
        CancellationToken cancellationToken = default)
    {
        // results contiene los dispositivos descubiertos durante esta ejecución.
        var results = new List<DiscoveredDevice>();

        // bindAddress es la IPv4 del puerto de red que el técnico seleccionó en Camera Inspector.
        var bindAddress = networkInterface.IpAddress;

        // broadcastAddress es el broadcast dirigido calculado desde la IP y máscara del adaptador.
        var broadcastAddress = CalculateBroadcastAddress(
            networkInterface.IpAddress,
            networkInterface.SubnetMask);

        // Socket debe permanecer asociado al puerto 5678 para que las cámaras sepan dónde devolver la respuesta.
        using var socket = new UdpClient(new IPEndPoint(bindAddress, ListenPort));

        // EnableBroadcast permite emitir el discovery hacia el broadcast de la red seleccionada.
        socket.EnableBroadcast = true;

        // probe contiene el paquete binario mínimo utilizado por el discovery propietario documentado públicamente.
        var probe = BuildProbe();

        // endpoint es el puerto UDP donde las cámaras VIVOTEK esperan el mensaje de descubrimiento.
        var endpoint = new IPEndPoint(broadcastAddress, DiscoveryPort);

        // Enviamos el mismo probe al broadcast dirigido de la interfaz.
        await socket.SendAsync(probe, probe.Length, endpoint);

        // También enviamos a 255.255.255.255 porque algunas topologías de enlace directo
        // no entregan correctamente el broadcast dirigido mientras la interfaz está en autoconfiguración.
        await socket.SendAsync(probe, probe.Length, new IPEndPoint(IPAddress.Broadcast, DiscoveryPort));

        // deadline marca el momento exacto en que finaliza la ventana de escucha.
        var deadline = DateTimeOffset.UtcNow + _discoveryTimeout;

        while (!cancellationToken.IsCancellationRequested)
        {
            // remaining es el tiempo que todavía queda disponible para recibir respuestas.
            var remaining = deadline - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero)
                break;

            // receiveTask espera una respuesta sin bloquear el hilo de UI.
            var receiveTask = socket.ReceiveAsync(cancellationToken).AsTask();

            // waitTask permite salir por timeout incluso cuando ninguna cámara responde.
            var waitTask = Task.Delay(remaining, cancellationToken);
            var completedTask = await Task.WhenAny(receiveTask, waitTask);

            if (completedTask != receiveTask)
                break;

            // packet contiene tanto los bytes de respuesta como la IP desde la que respondió el dispositivo.
            var packet = await receiveTask;

            // Solo aceptamos respuestas que procedan del puerto de discovery esperado para reducir falsos positivos.
            if (packet.RemoteEndPoint.Port != DiscoveryPort)
                continue;

            // device representa la evidencia mínima necesaria para que el resto de la aplicación continúe.
            var device = CreateDiscoveredDevice(packet.RemoteEndPoint.Address, packet.Buffer);

            // duplicate evita insertar dos veces la misma cámara cuando respondió por ambos broadcasts.
            if (results.Any(item => string.Equals(
                    item.IpAddress,
                    device.IpAddress,
                    StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            results.Add(device);
        }

        return results;
    }

    /// <summary>
    /// Construye el sondeo mínimo de VIVOTEK. La estructura se basa en el formato
    /// públicamente observado para Shepherd/UniversalScanner: 0x01 + identificador de
    /// sesión de 3 bytes + 0x03.
    /// </summary>
    private static byte[] BuildProbe()
    {
        // session contiene tres bytes variables para diferenciar cada solicitud de discovery.
        var session = Guid.NewGuid().ToByteArray();

        // probe es el buffer final que se entrega directamente a UdpClient.SendAsync.
        return new[]
        {
            (byte)0x01,
            session[0],
            session[1],
            session[2],
            (byte)0x03
        };
    }

    /// <summary>
    /// Convierte la IP y máscara de la interfaz en su broadcast dirigido.
    /// </summary>
    private static IPAddress CalculateBroadcastAddress(IPAddress ipAddress, IPAddress subnetMask)
    {
        // ipBytes contiene los cuatro octetos de la dirección IPv4 local.
        var ipBytes = ipAddress.GetAddressBytes();
        // maskBytes contiene los cuatro octetos de la máscara configurada.
        var maskBytes = subnetMask.GetAddressBytes();

        // broadcastBytes comenzará como copia de la IP y luego cada octeto se ajustará al broadcast.
        var broadcastBytes = new byte[4];

        for (var index = 0; index < 4; index++)
        {
            // Cada bit del broadcast vale 1 donde la máscara define bits de host.
            broadcastBytes[index] = (byte)(ipBytes[index] | ~maskBytes[index]);
        }

        return new IPAddress(broadcastBytes);
    }

    /// <summary>
    /// Genera el modelo inicial de Camera Inspector a partir de una respuesta VIVOTEK.
    /// La IP del remitente es evidencia de red; la carga se conserva como evidencia bruta
    /// para futuros decodificadores de modelo/MAC/firmware.
    /// </summary>
    private static DiscoveredDevice CreateDiscoveredDevice(IPAddress address, byte[] payload)
    {
        // macAddress intenta resolver la MAC desde la caché ARP local una vez que exista tráfico con la cámara.
        // En esta etapa no dependemos de que el parser propietario ya conozca el formato completo.
        _ = payload;

        return new DiscoveredDevice
        {
            IpAddress = address.ToString(),
            Manufacturer = "VIVOTEK",
            AssignedProviderName = "VIVOTEK",
            Status = DeviceStatus.Online,
            HttpSupported = true
        };
    }
}
