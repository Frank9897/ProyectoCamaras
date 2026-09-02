using CameraInspector.Core.Interfaces;

namespace CameraInspector.App.ViewModels;

public sealed partial class MainViewModel
{
    public Task<object?> RequestCredentialsForOperationAsync()
    {
        return RequestCredentialsCoreAsync();
    }

    private async Task<object?> RequestCredentialsCoreAsync()
    {
        var credentials = await GetCredentialsAsync();
        return credentials is null
            ? null
            : new OperationCredentialSession(credentials.Username, credentials.Password);
    }

    public sealed record OperationCredentialSession(string Username, string Password);

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
            Core.Models.CameraHealthState.Healthy => Core.Models.DeviceStatus.Online,
            Core.Models.CameraHealthState.NoResponse => Core.Models.DeviceStatus.Error,
            _ => Core.Models.DeviceStatus.Warning
        };
        SelectedDevice.RefreshHealth();
        SelectedDevice.Refresh();
    }
}
