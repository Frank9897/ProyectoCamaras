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
