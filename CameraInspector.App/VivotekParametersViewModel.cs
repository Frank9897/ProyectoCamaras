using System.Collections.ObjectModel;
using CameraInspector.App.ViewModels;
using CameraInspector.Core.Interfaces;
using CameraInspector.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CameraInspector.App;

/// <summary>
/// ViewModel para inspeccionar grupos de parámetros CGI VIVOTEK sin modificarlos.
/// </summary>
public sealed partial class VivotekParametersViewModel : ObservableObject
{
    private readonly DeviceViewModel _deviceViewModel;
    private readonly IVivotekParameterService _parameterService;
    private readonly ICredentialStore _credentialStore;
    private readonly ICameraCredentialStore _cameraCredentialStore;

    /// <summary>Grupos conocidos que sirven como punto de partida para el inspector.</summary>
    public ObservableCollection<string> Groups { get; } = new(["system.info", "image", "video", "network"]);

    /// <summary>Parámetros devueltos por la última consulta.</summary>
    public ObservableCollection<VivotekParameterItem> Parameters { get; } = new();

    [ObservableProperty]
    private string _selectedGroup = "system.info";

    [ObservableProperty]
    private string _statusText = "Listo para consultar parámetros VIVOTEK.";

    public event EventHandler? RequestClose;

    public VivotekParametersViewModel(
        DeviceViewModel deviceViewModel,
        IVivotekParameterService parameterService,
        ICredentialStore credentialStore,
        ICameraCredentialStore cameraCredentialStore)
    {
        // _deviceViewModel conserva el modelo de red y la identidad persistente de la cámara.
        _deviceViewModel = deviceViewModel;
        // _parameterService encapsula las peticiones CGI de lectura.
        _parameterService = parameterService;
        // _credentialStore recupera el secreto solo cuando el usuario consulta un grupo.
        _credentialStore = credentialStore;
        // _cameraCredentialStore localiza la referencia segura de la cámara.
        _cameraCredentialStore = cameraCredentialStore;
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        Parameters.Clear();

        if (_deviceViewModel.CameraId is not int cameraId)
        {
            StatusText = "La cámara todavía no tiene identidad persistente en el inventario.";
            return;
        }

        var savedInfo = await _cameraCredentialStore.GetAsync(cameraId);
        if (savedInfo is null)
        {
            StatusText = "No hay credenciales guardadas para esta cámara.";
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
            // parameters contiene exactamente los pares clave=valor devueltos por el firmware.
            var parameters = await _parameterService.GetGroupAsync(
                _deviceViewModel.Device,
                credentials.Username,
                credentials.Password,
                SelectedGroup);

            foreach (var parameter in parameters)
                Parameters.Add(parameter);

            StatusText = $"Grupo '{SelectedGroup}': {parameters.Count} parámetros encontrados.";
        }
        catch (Exception ex)
        {
            StatusText = $"Error al consultar VIVOTEK: {ex.Message}";
        }
    }

    [RelayCommand]
    private void Close() => RequestClose?.Invoke(this, EventArgs.Empty);
}
