using CameraInspector.Core.Interfaces;
using CameraInspector.Core.Models;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;

namespace CameraInspector.App.ViewModels;

/// <summary>
/// Comandos PTZ de la pantalla principal.
/// Se mantiene como archivo parcial para no inflar MainViewModel.cs.
/// </summary>
public sealed partial class MainViewModel
{
    private IOnvifPtzService? PtzService =>
        CameraInspector.App.App.Services?.GetService<IOnvifPtzService>();

    [RelayCommand]
    private Task PtzLeftAsync() => MovePtzAsync(new OnvifPtzMoveRequest { Pan = -0.5f });

    [RelayCommand]
    private Task PtzRightAsync() => MovePtzAsync(new OnvifPtzMoveRequest { Pan = 0.5f });

    [RelayCommand]
    private Task PtzUpAsync() => MovePtzAsync(new OnvifPtzMoveRequest { Tilt = 0.5f });

    [RelayCommand]
    private Task PtzDownAsync() => MovePtzAsync(new OnvifPtzMoveRequest { Tilt = -0.5f });

    [RelayCommand]
    private Task PtzZoomInAsync() => MovePtzAsync(new OnvifPtzMoveRequest { Zoom = 0.5f });

    [RelayCommand]
    private Task PtzZoomOutAsync() => MovePtzAsync(new OnvifPtzMoveRequest { Zoom = -0.5f });

    [RelayCommand]
    private async Task PtzStopAsync()
    {
        if (SelectedDevice is null || PtzService is null)
            return;

        var credentials = await GetCredentialsAsync();
        if (credentials is null)
            return;

        try
        {
            var success = await PtzService.StopAsync(
                SelectedDevice.Device,
                credentials.Username,
                credentials.Password);

            StatusText = success
                ? "PTZ detenido."
                : "La cámara rechazó la orden PTZ o el servicio no está disponible.";
        }
        catch (Exception ex)
        {
            StatusText = $"Error al detener PTZ: {ex.Message}";
        }
    }

    private async Task MovePtzAsync(OnvifPtzMoveRequest request)
    {
        if (SelectedDevice is null || PtzService is null)
            return;

        var credentials = await GetCredentialsAsync();
        if (credentials is null)
            return;

        try
        {
            // movement indica el eje solicitado y la velocidad normalizada enviada al servicio ONVIF.
            var success = await PtzService.ContinuousMoveAsync(
                SelectedDevice.Device,
                request,
                credentials.Username,
                credentials.Password);

            StatusText = success
                ? "Orden PTZ enviada. Pulse DETENER para finalizar el movimiento."
                : "La cámara rechazó la orden PTZ o el servicio no está disponible.";
        }
        catch (Exception ex)
        {
            StatusText = $"Error PTZ: {ex.Message}";
        }
    }
}
