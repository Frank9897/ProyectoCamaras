using System.Net;
using CameraInspector.Core.Interfaces;
using CameraInspector.Core.Models;

namespace CameraInspector.Network.Providers.Vivotek;

/// <summary>
/// Inspector de parámetros CGI VIVOTEK en modo exclusivamente lectura.
/// </summary>
public sealed class VivotekParameterService : IVivotekParameterService
{
    /// <summary>Timeout por petición; un inspector no debe bloquear la aplicación indefinidamente.</summary>
    private readonly TimeSpan _timeout = TimeSpan.FromSeconds(4);

    public async Task<IReadOnlyList<VivotekParameterItem>> GetGroupAsync(
        DiscoveredDevice device,
        string username,
        string password,
        string group,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(device.IpAddress) || string.IsNullOrWhiteSpace(group))
            return [];

        var encodedGroup = Uri.EscapeDataString(group.Trim());
        var port = device.HttpPort ?? 80;

        // Primero intentamos anonymous para no exigir credenciales cuando el firmware lo permite.
        var anonymous = await SendAsync(
            device.IpAddress.Trim(),
            port,
            $"/cgi-bin/anonymous/getparam.cgi?{encodedGroup}",
            string.Empty,
            string.Empty,
            cancellationToken);

        if (anonymous.Success && !string.IsNullOrWhiteSpace(anonymous.Body))
            return Parse(group.Trim(), anonymous.Body);

        // Algunos firmwares responden correctamente a anonymous para ciertos grupos pero
        // protegen otros. En ese caso reintentamos como admin usando Basic/Digest negociado.
        if (string.IsNullOrWhiteSpace(username))
            return [];

        var admin = await SendAsync(
            device.IpAddress.Trim(),
            port,
            $"/cgi-bin/admin/getparam.cgi?{encodedGroup}",
            username,
            password,
            cancellationToken);

        return admin.Success && !string.IsNullOrWhiteSpace(admin.Body)
            ? Parse(group.Trim(), admin.Body)
            : [];
    }

    private async Task<(bool Success, string Body)> SendAsync(
        string ip,
        int port,
        string relativePath,
        string username,
        string password,
        CancellationToken cancellationToken)
    {
        using var handler = new HttpClientHandler
        {
            Credentials = new NetworkCredential(username, password),
            PreAuthenticate = false,
            AllowAutoRedirect = false,
            UseProxy = false
        };

        using var client = new HttpClient(handler) { Timeout = _timeout };
        client.DefaultRequestHeaders.ConnectionClose = true;
        client.DefaultRequestHeaders.UserAgent.ParseAdd("CameraInspector/1.0");

        try
        {
            using var response = await client.GetAsync(
                $"http://{ip}:{port}{relativePath}",
                cancellationToken);

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            return (response.IsSuccessStatusCode, body);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return (false, string.Empty);
        }
        catch (HttpRequestException)
        {
            return (false, string.Empty);
        }
    }

    /// <summary>
    /// Convierte una respuesta CGI de texto en elementos de parámetro sin interpretar tipos específicos.
    /// </summary>
    internal static IReadOnlyList<VivotekParameterItem> Parse(string group, string body)
    {
        var items = new List<VivotekParameterItem>();

        foreach (var rawLine in body.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            var separator = line.IndexOf('=');

            if (separator <= 0)
                continue;

            var name = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();

            if (string.IsNullOrWhiteSpace(name))
                continue;

            items.Add(new VivotekParameterItem
            {
                Group = group,
                Name = name,
                Value = value
            });
        }

        return items;
    }
}
