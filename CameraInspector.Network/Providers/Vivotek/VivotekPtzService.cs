using System.Net;
using CameraInspector.Core.Interfaces;

namespace CameraInspector.Network.Providers.Vivotek;

/// <summary>
/// Implementación del control PTZ propietario de VIVOTEK mediante CGI.
/// Se prueban dos rutas históricas para mejorar compatibilidad entre generaciones de firmware.
/// </summary>
public sealed class VivotekPtzService : IVivotekPtzService
{
    private readonly TimeSpan _timeout = TimeSpan.FromSeconds(4);

    public async Task<bool> MoveAsync(
        string ipAddress,
        string username,
        string password,
        VivotekPtzMove move,
        CancellationToken cancellationToken = default)
    {
        // command representa el valor definido por la API VIVOTEK para el movimiento solicitado.
        var command = move switch
        {
            VivotekPtzMove.Up => "up",
            VivotekPtzMove.Down => "down",
            VivotekPtzMove.Left => "left",
            VivotekPtzMove.Right => "right",
            VivotekPtzMove.Home => "home",
            _ => throw new ArgumentOutOfRangeException(nameof(move))
        };

        // speedpan y speedtilt se mantienen en un valor moderado para evitar movimientos excesivamente bruscos.
        var query = $"move={command}&speedpan=2&speedtilt=2";
        return await ExecuteAsync(ipAddress, username, password, query, cancellationToken);
    }

    public Task<bool> StopAsync(
        string ipAddress,
        string username,
        string password,
        CancellationToken cancellationToken = default)
    {
        // auto=stop solicita a la cámara detener la acción PTZ actual según la API VIVOTEK.
        return ExecuteAsync(ipAddress, username, password, "auto=stop&zoom=stop", cancellationToken);
    }

    public Task<bool> ZoomWideAsync(
        string ipAddress,
        string username,
        string password,
        CancellationToken cancellationToken = default)
    {
        // zoom=wide solicita una vista más amplia utilizando la velocidad configurada por la cámara.
        return ExecuteAsync(ipAddress, username, password, "speedzoom=2&zoom=wide", cancellationToken);
    }

    public Task<bool> ZoomTeleAsync(
        string ipAddress,
        string username,
        string password,
        CancellationToken cancellationToken = default)
    {
        // zoom=tele solicita ampliar la escena utilizando la velocidad configurada por la cámara.
        return ExecuteAsync(ipAddress, username, password, "speedzoom=2&zoom=tele", cancellationToken);
    }

    private async Task<bool> ExecuteAsync(
        string ipAddress,
        string username,
        string password,
        string query,
        CancellationToken cancellationToken)
    {
        // ipAddress es la dirección de la cámara VIVOTEK seleccionada por el técnico.
        ArgumentException.ThrowIfNullOrWhiteSpace(ipAddress);

        using var handler = new HttpClientHandler
        {
            // Las credenciales se utilizan únicamente durante la operación solicitada.
            Credentials = new NetworkCredential(username, password),
            PreAuthenticate = false,
            // No se siguen redirecciones para impedir el envío de credenciales fuera del destino original.
            AllowAutoRedirect = false
        };

        using var client = new HttpClient(handler)
        {
            Timeout = _timeout
        };

        // Algunas generaciones exponen camctrl bajo viewer y otras bajo camctrl; probamos ambas rutas.
        var endpoints = new[]
        {
            $"http://{ipAddress.Trim()}/cgi-bin/viewer/camctrl.cgi?{query}",
            $"http://{ipAddress.Trim()}/cgi-bin/camctrl/camctrl.cgi?{query}",
            $"http://{ipAddress.Trim()}/cgi-bin/camctrl.cgi?{query}"
        };

        foreach (var endpoint in endpoints)
        {
            // response contiene el resultado HTTP de la cámara para el comando solicitado.
            using var response = await client.GetAsync(endpoint, cancellationToken);

            if (response.IsSuccessStatusCode)
                return true;

            // Un 404/405 es una señal razonable para intentar la siguiente ruta histórica.
            if ((int)response.StatusCode != 404 && (int)response.StatusCode != 405)
                return false;
        }

        return false;
    }
}
