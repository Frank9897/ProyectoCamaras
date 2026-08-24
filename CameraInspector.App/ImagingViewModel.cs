using System.Collections.ObjectModel;
using CameraInspector.Core.Interfaces;
using CameraInspector.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CameraInspector.App;

/// <summary>
/// ViewModel de la ventana Imaging.
/// Mantiene los ajustes en memoria y delega la comunicación ONVIF al servicio de Core/Network.
/// </summary>
public sealed partial class ImagingViewModel : ObservableObject
{
    private readonly DiscoveredDevice _device;
    private readonly IOnvifImagingService _imagingService;
    private readonly ICredentialStore _credentialStore;
    private readonly ICameraCredentialStore _cameraCredentialStore;

    [ObservableProperty]
    private OnvifImagingSettings _settings = new();

    [ObservableProperty]
    private string _statusText = "Listo para leer los ajustes.";

    public ObservableCollection<string> IrCutOptions { get; } = new(["ON", "OFF", "AUTO"]);

    public event EventHandler? RequestClose;

    public ImagingViewModel(
        DiscoveredDevice device,
        IOnvifImagingService imagingService,
        ICredentialStore credentialStore,
        ICameraCredentialStore cameraCredentialStore)
    {
        // _device identifica la cámara que se está configurando.
        _device = device;
        // _imagingService encapsula SOAP ONVIF.
        _imagingService = imagingService;
        // _credentialStore recupera el secreto desde Windows Credential Manager.
        _credentialStore = credentialStore;
        // _cameraCredentialStore localiza la referencia de credencial asociada a la cámara.
        _cameraCredentialStore = cameraCredentialStore;
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        try
        {
            var credentials = await GetCredentialsAsync();
            if (credentials is null)
                return;

            // loaded contiene la configuración que la cámara reporta actualmente.
            var loaded = await _imagingService.GetImagingSettingsAsync(
                _device,
                credentials.Value.Username,
                credentials.Value.Password);

            if (loaded is null)
            {
                StatusText = "La cámara no devolvió ajustes Imaging.";
                return;
            }

            Settings = loaded;
            StatusText = "Ajustes Imaging cargados correctamente.";
        }
        catch (Exception ex)
        {
            StatusText = $"Error al leer Imaging: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        try
        {
            var credentials = await GetCredentialsAsync();
            if (credentials is null)
                return;

            var saved = await _imagingService.SetImagingSettingsAsync(
                _device,
                Settings,
                credentials.Value.Username,
                credentials.Value.Password);

            StatusText = saved
                ? "Ajustes Imaging aplicados correctamente."
                : "La cámara rechazó o no soporta el cambio solicitado.";
        }
        catch (Exception ex)
        {
            StatusText = $"Error al aplicar Imaging: {ex.Message}";
        }
    }

    [RelayCommand]
    private void Close() => RequestClose?.Invoke(this, EventArgs.Empty);

    private async Task<(string Username, string Password)?> GetCredentialsAsync()
    {
        if (_device.CameraId is not int cameraId)
        {
            StatusText = "La cámara aún no tiene identidad persistente para reutilizar credenciales.";
            return null;
        }

        var savedInfo = await _cameraCredentialStore.GetAsync(cameraId);
        if (savedInfo is null)
        {
            StatusText = "No hay credenciales guardadas para esta cámara. Guárdelas desde el panel principal.";
            return null;
        }

        var stored = await _credentialStore.GetAsync(savedInfo.CredentialRef);
        if (stored is null)
        {
            StatusText = "La credencial guardada ya no existe en Windows Credential Manager.";
            return null;
        }

        return (stored.Username, stored.Password);
    }
}
