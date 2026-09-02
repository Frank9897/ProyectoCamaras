using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace CameraInspector.Network;

/// <summary>
/// Prepara temporalmente una dirección link-local APIPA en la interfaz seleccionada.
/// VIVOTEK utiliza una dirección 169.254.x.x como Alias IP cuando no existe DHCP,
/// y sus herramientas de descubrimiento asignan una dirección APIPA al PC para poder
/// comunicarse con la cámara directamente.
/// </summary>
public static class VivotekDirectLinkAddressService
{
    private const string ApipaPrefix = "169.254.";
    private const string ApipaMask = "255.255.0.0";
    private static readonly Dictionary<string, IPAddress> TemporaryAddresses = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object Sync = new();

    public static async Task<(bool Success, IPAddress? Address, string Message)> EnsureApipaAddressAsync(
        string interfaceName,
        string interfaceId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(interfaceName) || string.IsNullOrWhiteSpace(interfaceId))
            return (false, null, "No se pudo identificar la interfaz Ethernet para preparar el enlace directo.");

        var existing = GetExistingApipa(interfaceId);
        if (existing is not null)
        {
            return (true, existing, $"La interfaz ya dispone de una dirección APIPA {existing}.");
        }

        var candidate = FindFreeApipaAddress();
        if (candidate is null)
        {
            return (false, null, "No se encontró una dirección APIPA local disponible.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        var result = await RunNetshAsync(
            ["interface", "ipv4", "add", "address", $"name={interfaceName}", $"address={candidate}", $"mask={ApipaMask}", "store=active"],
            cancellationToken);

        if (result.ExitCode != 0)
        {
            return (false, null, $"Windows no pudo asignar la IP APIPA temporal a '{interfaceName}'. Código netsh: {result.ExitCode}. {result.Error}");
        }

        // Damos tiempo a Windows para instalar la ruta link-local antes de abrir los sockets UDP.
        await Task.Delay(250, cancellationToken);

        var assigned = GetExistingApipa(interfaceId, candidate);
        if (assigned is null)
        {
            await RemoveAddressAsync(interfaceName, candidate, cancellationToken);
            return (false, null, "Windows informó que la IP APIPA fue agregada, pero la interfaz todavía no la muestra como activa.");
        }

        lock (Sync)
        {
            TemporaryAddresses[interfaceId] = assigned;
        }

        return (true, assigned, $"APIPA temporal {assigned} asignada a '{interfaceName}' para discovery directo VIVOTEK.");
    }

    public static async Task RemoveTemporaryAddressesAsync(CancellationToken cancellationToken = default)
    {
        KeyValuePair<string, IPAddress>[] entries;
        lock (Sync)
        {
            entries = TemporaryAddresses.ToArray();
            TemporaryAddresses.Clear();
        }

        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var interfaceName = ResolveInterfaceName(entry.Key);
            if (interfaceName is null)
                continue;

            await RemoveAddressAsync(interfaceName, entry.Value, cancellationToken);
        }
    }

    private static IPAddress? GetExistingApipa(string interfaceId, IPAddress? expected = null)
    {
        try
        {
            var nic = NetworkInterface.GetAllNetworkInterfaces()
                .FirstOrDefault(item => item.Id.Equals(interfaceId, StringComparison.OrdinalIgnoreCase));

            if (nic is null)
                return null;

            return nic.GetIPProperties()
                .UnicastAddresses
                .Select(item => item.Address)
                .Where(IsApipa)
                .FirstOrDefault(address => expected is null || address.Equals(expected));
        }
        catch
        {
            return null;
        }
    }

    private static string? ResolveInterfaceName(string interfaceId)
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .FirstOrDefault(item => item.Id.Equals(interfaceId, StringComparison.OrdinalIgnoreCase))?.Name;
        }
        catch
        {
            return null;
        }
    }

    private static IPAddress? FindFreeApipaAddress()
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                foreach (var address in nic.GetIPProperties().UnicastAddresses)
                {
                    if (IsApipa(address.Address))
                        used.Add(address.Address.ToString());
                }
            }
        }
        catch
        {
        }

        for (var thirdOctet = 1; thirdOctet <= 254; thirdOctet++)
        {
            for (var fourthOctet = 2; fourthOctet <= 254; fourthOctet++)
            {
                var candidate = $"169.254.{thirdOctet}.{fourthOctet}";
                if (!used.Contains(candidate) && IPAddress.TryParse(candidate, out var address))
                    return address;
            }
        }

        return null;
    }

    private static bool IsApipa(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return bytes.Length == 4 && bytes[0] == 169 && bytes[1] == 254;
    }

    private static async Task<(int ExitCode, string Output, string Error)> RunNetshAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "netsh.exe",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = Environment.SystemDirectory
        };

        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        var output = await outputTask;
        var error = await errorTask;
        return (process.ExitCode, output.Trim(), error.Trim());
    }

    private static async Task RemoveAddressAsync(
        string interfaceName,
        IPAddress address,
        CancellationToken cancellationToken)
    {
        try
        {
            await RunNetshAsync(
                ["interface", "ipv4", "delete", "address", $"name={interfaceName}", $"address={address}", "store=active"],
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
        }
    }
}
