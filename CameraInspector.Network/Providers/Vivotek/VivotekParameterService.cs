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
        // group define el conjunto CGI que queremos consultar, por ejemplo "image" o "system.info".
        if (string.IsNullOrWhiteSpace(device.IpAddress) || string.IsNullOrWhiteSpace(group))
            return [];

        // El grupo se codifica como query para evitar problemas con caracteres reservados.
        var encodedGroup = Uri.EscapeDataString(group.Trim());

        using var handler = new HttpClientHandler
        {
            // Las credenciales se usan únicamente cuando el técnico solicita esta operación.
            Credentials = new NetworkCredential(username, password),
            PreAuthenticate = false,
            // No seguimos redirecciones para no reenviar credenciales a otro destino.
            AllowAutoRedirect = false
        };

        using var client = new HttpClient(handler)
        {
            Timeout = _timeout
        };

        // anonymous es la ruta más restrictiva para lectura y permite trabajar con cámaras que exponen
        // el grupo sin exigir privilegios de operador/admin para una consulta.
        var endpoint = $"http://{device.IpAddress.Trim()}/cgi-bin/anonymous/getparam.cgi?{encodedGroup}";

        using var response = await client.GetAsync(endpoint, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return [];

        // body contiene líneas del tipo parámetro=valor.
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(body))
            return [];

        return Parse(group.Trim(), body);
    }

    /// <summary>
    /// Convierte una respuesta CGI de texto en elementos de parámetro sin interpretar tipos específicos.
    /// </summary>
    internal static IReadOnlyList<VivotekParameterItem> Parse(string group, string body)
    {
        // items conserva todos los parámetros reconocibles, incluso si el firmware devuelve valores desconocidos.
        var items = new List<VivotekParameterItem>();

        foreach (var rawLine in body.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            // line elimina espacios exteriores pero conserva el contenido del valor.
            var line = rawLine.Trim();
            // separator separa el nombre del valor en la primera aparición de '='.
            var separator = line.IndexOf('=');

            if (separator <= 0)
                continue;

            // name es el identificador exacto del parámetro devuelto por la cámara.
            var name = line[..separator].Trim();
            // value es el contenido textual informado por el firmware.
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
