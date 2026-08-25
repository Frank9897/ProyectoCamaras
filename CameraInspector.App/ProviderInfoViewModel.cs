using CameraInspector.App.ViewModels;
using CameraInspector.Core.Interfaces;
using CameraInspector.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CameraInspector.App;

/// <summary>
/// ViewModel para consultar información propietaria de la cámara mediante el provider detectado.
/// </summary>
public sealed partial class ProviderInfoViewModel : ObservableObject
{
    private readonly DeviceViewModel _deviceViewModel;
    private readonly ICameraProviderResolver _providerResolver;
    private readonly ICredentialStore _credentialStore;
    private readonly ICameraCredentialStore _cameraCredentialStore;

    [ObservableProperty]
    private string _statusText = "Preparando provider...";

    [ObservableProperty]
    private CameraProviderInfo? _providerInfo;

    [ObservableProperty]
    private string _providerName = "Sin provider compatible";

    public event EventHandler? RequestClose;

    public ProviderInfoViewModel(
        DeviceViewModel deviceViewModel,
        ICameraProviderResolver providerResolver,
        ICredentialStore credentialStore,
        ICameraCredentialStore cameraCredentialStore)
    {
        // _deviceViewModel conserva el dispositivo y su CameraId persistente.
        _deviceViewModel = deviceViewModel;
        // _providerResolver selecciona el protocolo propietario compatible sin autenticar.
        _providerResolver = providerResolver;
        // _credentialStore recupera la contraseña únicamente cuando el usuario solicita la operación.
        _credentialStore = credentialStore;
        // _cameraCredentialStore localiza la referencia segura asociada a la cámara.
        _cameraCredentialStore = cameraCredentialStore;
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        var provider = _providerResolver.Resolve(_deviceViewModel.Device);
        if (provider is null)
        {
            ProviderName = "Sin provider compatible";
            StatusText = "No hay un provider propietario disponible para esta cámara.";
            return;
        }

        ProviderName = provider.Name;

        if (_deviceViewModel.CameraId is not int cameraId)
        {
            StatusText = "La cámara aún no tiene identidad persistente.";
            return;
        }

        var savedInfo = await _cameraCredentialStore.GetAsync(cameraId);
        if (savedInfo is null)
        {
            StatusText = "No hay credenciales guardadas. Guárdelas desde el panel principal para consultar el provider.";
            return;
        }

        var credentials = await _credentialStore.GetAsync(savedInfo.CredentialRef);
        if (credentials is null)
        {
            StatusText = "La credencial asociada ya no existe en Windows Credential Manager.";
            return;
        }

        try
        {
            // providerInfo contiene exclusivamente información de lectura devuelta por el fabricante.
            ProviderInfo = await provider.GetDeviceInfoAsync(
                _deviceViewModel.Device,
                credentials.Username,
                credentials.Password);

            StatusText = ProviderInfo is null
                ? "El provider no pudo obtener información del dispositivo."
                : "Información propietaria obtenida correctamente.";
        }
        catch (Exception ex)
        {
            StatusText = $"Error del provider: {ex.Message}";
        }
    }

    [RelayCommand]
    private void Close() => RequestClose?.Invoke(this, EventArgs.Empty);
}
