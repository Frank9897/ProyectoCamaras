using CameraInspector.App.ViewModels;
using CameraInspector.Core.Interfaces;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CameraInspector.App;

/// <summary>
/// ViewModel para el control PTZ propietario de VIVOTEK.
/// </summary>
public sealed partial class VivotekPtzViewModel : ObservableObject
{
    private readonly DeviceViewModel _deviceViewModel;
    private readonly IVivotekPtzService _ptzService;
    private readonly ICredentialStore _credentialStore;
    private readonly ICameraCredentialStore _cameraCredentialStore;

    [ObservableProperty]
    private string _statusText = "Listo para controlar PTZ VIVOTEK.";

    public event EventHandler? RequestClose;

    public VivotekPtzViewModel(
        DeviceViewModel deviceViewModel,
        IVivotekPtzService ptzService,
        ICredentialStore credentialStore,
        ICameraCredentialStore cameraCredentialStore)
    {
        // _deviceViewModel conserva IP, identidad persistente y datos de la cámara seleccionada.
        _deviceViewModel = deviceViewModel;
        // _ptzService ejecuta los comandos CGI propietarios.
        _ptzService = ptzService;
        // _credentialStore recupera el secreto solo durante la acción autorizada.
        _credentialStore = credentialStore;
        // _cameraCredentialStore relaciona la cámara con su CredentialRef.
        _cameraCredentialStore = cameraCredentialStore;
    }

    [RelayCommand]
    private Task UpAsync() => ExecuteAsync(
        "Subiendo cámara...",
        (username, password, cancellationToken) => _ptzService.MoveAsync(
            _deviceViewModel.IpAddress,
            username,
            password,
            VivotekPtzMove.Up,
            cancellationToken));

    [RelayCommand]
    private Task DownAsync() => ExecuteAsync(
        "Bajando cámara...",
        (username, password, cancellationToken) => _ptzService.MoveAsync(
            _deviceViewModel.IpAddress,
            username,
            password,
            VivotekPtzMove.Down,
            cancellationToken));

    [RelayCommand]
    private Task LeftAsync() => ExecuteAsync(
        "Moviendo a la izquierda...",
        (username, password, cancellationToken) => _ptzService.MoveAsync(
            _deviceViewModel.IpAddress,
            username,
            password,
            VivotekPtzMove.Left,
            cancellationToken));

    [RelayCommand]
    private Task RightAsync() => ExecuteAsync(
        "Moviendo a la derecha...",
        (username, password, cancellationToken) => _ptzService.MoveAsync(
            _deviceViewModel.IpAddress,
            username,
            password,
            VivotekPtzMove.Right,
            cancellationToken));

    [RelayCommand]
    private Task HomeAsync() => ExecuteAsync(
        "Buscando posición Home...",
        (username, password, cancellationToken) => _ptzService.MoveAsync(
            _deviceViewModel.IpAddress,
            username,
            password,
            VivotekPtzMove.Home,
            cancellationToken));

    [RelayCommand]
    private Task StopAsync() => ExecuteAsync(
        "Deteniendo PTZ...",
        (username, password, cancellationToken) => _ptzService.StopAsync(
            _deviceViewModel.IpAddress,
            username,
            password,
            cancellationToken));

    [RelayCommand]
    private Task ZoomWideAsync() => ExecuteAsync(
        "Aplicando zoom wide...",
        (username, password, cancellationToken) => _ptzService.ZoomWideAsync(
            _deviceViewModel.IpAddress,
            username,
            password,
            cancellationToken));

    [RelayCommand]
    private Task ZoomTeleAsync() => ExecuteAsync(
        "Aplicando zoom tele...",
        (username, password, cancellationToken) => _ptzService.ZoomTeleAsync(
            _deviceViewModel.IpAddress,
            username,
            password,
            cancellationToken));

    [RelayCommand]
    private void Close() => RequestClose?.Invoke(this, EventArgs.Empty);

    private async Task ExecuteAsync(
        string pendingStatus,
        Func<string, string, CancellationToken, Task<bool>> action)
    {
        try
        {
            StatusText = pendingStatus;

            var credentials = await GetCredentialsAsync();
            if (credentials is null)
                return;

            // saved indica que la cámara aceptó la solicitud HTTP del comando PTZ.
            var saved = await action(credentials.Value.Username, credentials.Value.Password, CancellationToken.None);
            StatusText = saved
                ? "Comando PTZ enviado correctamente."
                : "La cámara rechazó el comando o esta ruta CGI no está disponible.";
        }
        catch (Exception ex)
        {
            StatusText = $"Error PTZ VIVOTEK: {ex.Message}";
        }
    }

    private async Task<(string Username, string Password)?> GetCredentialsAsync()
    {
        if (_deviceViewModel.CameraId is not int cameraId)
        {
            StatusText = "La cámara aún no tiene identidad persistente.";
            return null;
        }

        var savedInfo = await _cameraCredentialStore.GetAsync(cameraId);
        if (savedInfo is null)
        {
            StatusText = "No hay credenciales guardadas para esta cámara.";
            return null;
        }

        var credentials = await _credentialStore.GetAsync(savedInfo.CredentialRef);
        if (credentials is null)
        {
            StatusText = "La credencial asociada ya no existe en Windows Credential Manager.";
            return null;
        }

        return (credentials.Username, credentials.Password);
    }
}
