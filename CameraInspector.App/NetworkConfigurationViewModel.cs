using System.Collections.ObjectModel;
using CameraInspector.App.ViewModels;
using CameraInspector.Core.Interfaces;
using CameraInspector.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CameraInspector.App;

/// <summary>
/// ViewModel de configuración de red ONVIF.
/// Esta etapa es exclusivamente de lectura para evitar cambios accidentales de IP.
/// </summary>
public sealed partial class NetworkConfigurationViewModel : ObservableObject
{
    private readonly DeviceViewModel _deviceViewModel;
    private readonly IOnvifDeviceService _onvifDeviceService;
    private readonly ICredentialStore _credentialStore;
    private readonly ICameraCredentialStore _cameraCredentialStore;

    [ObservableProperty]
    private string _statusText = "Listo para consultar la configuración de red.";

    [ObservableProperty]
    private OnvifNetworkConfiguration? _configuration;

    public ObservableCollection<OnvifNetworkInterfaceInfo> Interfaces { get; } = new();
    public ObservableCollection<OnvifNetworkProtocolInfo> Protocols { get; } = new();
    public ObservableCollection<string> Gateways { get; } = new();

    public event EventHandler? RequestClose;

    public NetworkConfigurationViewModel(
        DeviceViewModel deviceViewModel,
        IOnvifDeviceService onvifDeviceService,
        ICredentialStore credentialStore,
        ICameraCredentialStore cameraCredentialStore)
    {
        // _deviceViewModel conserva el dispositivo y su CameraId persistente.
        _deviceViewModel = deviceViewModel;
        // _onvifDeviceService ejecuta las consultas READ_SYSTEM del Device Service.
        _onvifDeviceService = onvifDeviceService;
        // _credentialStore recupera la contraseña solo cuando el técnico solicita la consulta.
        _credentialStore = credentialStore;
        // _cameraCredentialStore resuelve la referencia almacenada para esta cámara.
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

            // loaded contiene exclusivamente la configuración de red devuelta por ONVIF.
            var loaded = await _onvifDeviceService.GetNetworkConfigurationAsync(
                _deviceViewModel.Device,
                credentials.Value.Username,
                credentials.Value.Password);

            if (loaded is null)
            {
                StatusText = "La cámara no devolvió información de red ONVIF.";
                return;
            }

            Configuration = loaded;

            Interfaces.Clear();
            foreach (var item in loaded.Interfaces)
                Interfaces.Add(item);

            Protocols.Clear();
            foreach (var item in loaded.Protocols)
                Protocols.Add(item);

            Gateways.Clear();
            foreach (var gateway in loaded.IPv4Gateways)
                Gateways.Add(gateway);

            StatusText = $"Consulta completada: {Interfaces.Count} interfaces, {Protocols.Count} protocolos.";
        }
        catch (Exception ex)
        {
            StatusText = $"Error al consultar red: {ex.Message}";
        }
    }

    [RelayCommand]
    private void Close() => RequestClose?.Invoke(this, EventArgs.Empty);

    private async Task<(string Username, string Password)?> GetCredentialsAsync()
    {
        // La configuración de red se consulta autenticada; no intentamos credenciales durante discovery.
        if (_deviceViewModel.CameraId is not int cameraId)
        {
            StatusText = "La cámara todavía no tiene identidad persistente.";
            return null;
        }

        var savedInfo = await _cameraCredentialStore.GetAsync(cameraId);
        if (savedInfo is null)
        {
            StatusText = "No hay credenciales guardadas para esta cámara.";
            return null;
        }

        var stored = await _credentialStore.GetAsync(savedInfo.CredentialRef);
        if (stored is null)
        {
            StatusText = "La credencial asociada ya no existe en Windows Credential Manager.";
            return null;
        }

        return (stored.Username, stored.Password);
    }
}
