using CameraInspector.Core.Interfaces;
using CameraInspector.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace CameraInspector.App.ViewModels;

public sealed partial class MainViewModel
{
    public async Task<(string Username, string Password)?> RequestCredentialsForOperationAsync()
    {
        var credentials = await GetCredentialsAsync();
        return credentials is null ? null : (credentials.Username, credentials.Password);
    }

    /// <summary>
    /// Registra una reproducción real como evidencia de vídeo confirmado.
    /// La comprobación ligera puede no conocer la ruta RTSP propietaria de una cámara,
    /// pero si LibVLC está reproduciendo no debemos mostrar "SIN VIDEO".
    /// </summary>
    public void MarkVideoConfirmed(string? protocol = null, int? port = null, string? message = null)
    {
        if (SelectedDevice is null)
            return;

        var device = SelectedDevice.Device;
        device.HealthState = CameraHealthState.Healthy;
        device.CommunicationAvailable = true;
        device.VideoAvailable = true;
        device.AuthenticationRequired = false;
        device.CommunicationPort = port ?? device.CommunicationPort ?? 554;
        device.CommunicationProtocol = protocol ?? device.CommunicationProtocol ?? "RTSP";
        device.HealthMessage = message ?? "OK: reproducción de vídeo confirmada por LibVLC.";
        device.LastHealthCheckAt = DateTimeOffset.UtcNow;
        device.Status = DeviceStatus.Online;

        SelectedDevice.RefreshHealth();
        SelectedDevice.Refresh();
    }

    public async Task RecheckSelectedHealthAsync()
    {
        if (SelectedDevice is null)
            return;

        var healthService = App.Services?.GetService<ICameraHealthService>();
        if (healthService is null)
            return;

        var result = await healthService.CheckAsync(SelectedDevice.Device);
        var device = SelectedDevice.Device;
        device.HealthState = result.State;
        device.CommunicationAvailable = result.CommunicationAvailable;
        device.VideoAvailable = result.VideoAvailable;
        device.AuthenticationRequired = result.AuthenticationRequired;
        device.CommunicationPort = result.CommunicationPort;
        device.CommunicationProtocol = result.Protocol;
        device.HealthMessage = result.Message;
        device.LastHealthCheckAt = result.CheckedAt;
        device.Status = result.State switch
        {
            CameraHealthState.Healthy => DeviceStatus.Online,
            CameraHealthState.NoResponse => DeviceStatus.Error,
            _ => DeviceStatus.Warning
        };
        SelectedDevice.RefreshHealth();
        SelectedDevice.Refresh();
    }
}
