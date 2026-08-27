using System.Collections.ObjectModel;
using System.Net;
using System.Windows;
using CameraInspector.App.ViewModels;
using CameraInspector.Core.Interfaces;
using CameraInspector.Core.Models;
using CameraInspector.Network.OnvifMedia;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CameraInspector.App;

/// <summary>
/// ViewModel específico para edición controlada de red ONVIF.
/// Se mantiene separado del ViewModel histórico de solo lectura para reducir riesgo de regresión.
/// </summary>
public sealed partial class NetworkConfigurationEditViewModel : ObservableObject
{
    private readonly DeviceViewModel _deviceViewModel;
    private readonly IOnvifDeviceService _onvifDeviceService;
    private readonly ICredentialStore _credentialStore;
    private readonly ICameraCredentialStore _cameraCredentialStore;
    private readonly IOnvifNetworkConfigurationService _writer;

    [ObservableProperty]
    private string _statusText = "Listo para consultar la configuración de red.";

    [ObservableProperty]
    private OnvifNetworkConfiguration? _configuration;

    [ObservableProperty]
    private OnvifNetworkInterfaceInfo? _selectedInterface;

    [ObservableProperty]
    private bool _useDhcp;

    [ObservableProperty]
    private string _ipv4Address = string.Empty;

    [ObservableProperty]
    private string _prefixLength = "24";

    [ObservableProperty]
    private string _gatewayAddress = string.Empty;

    [ObservableProperty]
    private bool _isApplying;

    public ObservableCollection<OnvifNetworkInterfaceInfo> Interfaces { get; } = new();
    public ObservableCollection<OnvifNetworkProtocolInfo> Protocols { get; } = new();
    public ObservableCollection<string> Gateways { get; } = new();

    public event EventHandler? RequestClose;

    public NetworkConfigurationEditViewModel(
        DeviceViewModel deviceViewModel,
        IOnvifDeviceService onvifDeviceService,
        ICredentialStore credentialStore,
        ICameraCredentialStore cameraCredentialStore)
    {
        // _deviceViewModel conserva la cámara seleccionada y su identidad persistente.
        _deviceViewModel = deviceViewModel;
        // _onvifDeviceService mantiene las operaciones de lectura ya existentes.
        _onvifDeviceService = onvifDeviceService;
        // _credentialStore obtiene el secreto solo cuando una acción explícita lo necesita.
        _credentialStore = credentialStore;
        // _cameraCredentialStore localiza la referencia segura asociada a la cámara.
        _cameraCredentialStore = cameraCredentialStore;
        // _writer ejecuta exclusivamente SetNetworkInterfaces/SetNetworkDefaultGateway.
        _writer = new OnvifNetworkConfigurationService();
    }

    partial void OnSelectedInterfaceChanged(OnvifNetworkInterfaceInfo? value)
    {
        if (value is null)
            return;

        // Los valores editables se rellenan con el estado actual sin escribir todavía en la cámara.
        UseDhcp = value.IPv4Dhcp == true;
        IPv4Address = value.IPv4Address ?? string.Empty;
        PrefixLength = (value.IPv4PrefixLength ?? 24).ToString();
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        try
        {
            var credentials = await GetCredentialsAsync();
            if (credentials is null)
                return;

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
            foreach (var item in loaded.IPv4Gateways)
                Gateways.Add(item);

            SelectedInterface = Interfaces.FirstOrDefault();
            GatewayAddress = Gateways.FirstOrDefault() ?? string.Empty;
            StatusText = $"Consulta completada: {Interfaces.Count} interfaces, {Protocols.Count} protocolos.";
        }
        catch (Exception ex)
        {
            StatusText = $"Error al consultar red: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ApplyAsync()
    {
        if (SelectedInterface is null || IsApplying)
        {
            StatusText = "Seleccione una interfaz de red válida.";
            return;
        }

        if (!UseDhcp)
        {
            // ipv4 verifica que no enviemos una dirección no IPv4 al dispositivo.
            if (!IPAddress.TryParse(IPv4Address, out var ipv4)
                || ipv4.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
            {
                StatusText = "La IPv4 indicada no es válida.";
                return;
            }

            // prefix limita el rango permitido por ONVIF para IPv4 CIDR.
            if (!int.TryParse(PrefixLength, out var prefix) || prefix < 0 || prefix > 32)
            {
                StatusText = "El prefijo debe estar entre 0 y 32.";
                return;
            }
        }

        if (!string.IsNullOrWhiteSpace(GatewayAddress)
            && (!IPAddress.TryParse(GatewayAddress, out var gateway)
                || gateway.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork))
        {
            StatusText = "El gateway IPv4 indicado no es válido.";
            return;
        }

        // La confirmación muestra el antes/después para evitar una modificación accidental.
        var result = MessageBox.Show(
            $"Se modificará la red de la cámara:\n\n" +
            $"IP: {SelectedInterface.IPv4Address ?? "(DHCP)"} → {(UseDhcp ? "DHCP" : IPv4Address.Trim())}\n" +
            $"Prefijo: {SelectedInterface.IPv4PrefixLength?.ToString() ?? "?"} → {(UseDhcp ? "DHCP" : PrefixLength)}\n" +
            $"Gateway: {Gateways.FirstOrDefault() ?? "(sin gateway)"} → {(string.IsNullOrWhiteSpace(GatewayAddress) ? "(sin gateway)" : GatewayAddress.Trim())}\n\n" +
            "La cámara puede quedar inaccesible durante unos segundos. ¿Continuar?",
            "Camera Inspector — Confirmar modificación",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
        {
            StatusText = "Modificación cancelada.";
            return;
        }

        try
        {
            IsApplying = true;
            var credentials = await GetCredentialsAsync();
            if (credentials is null)
                return;

            int? prefix = null;
            if (!UseDhcp && int.TryParse(PrefixLength, out var prefixValue))
                prefix = prefixValue;

            StatusText = "Aplicando IPv4...";
            var interfaceResult = await _writer.SetIPv4Async(
                _deviceViewModel.Device,
                credentials.Value.Username,
                credentials.Value.Password,
                SelectedInterface.Token,
                UseDhcp,
                UseDhcp ? null : IPv4Address.Trim(),
                prefix);

            if (!interfaceResult.Succeeded)
            {
                StatusText = $"No se aplicó IPv4: {interfaceResult.Message}";
                return;
            }

            StatusText = "IPv4 aplicada. Aplicando gateway...";
            var gatewayResult = await _writer.SetDefaultGatewayAsync(
                _deviceViewModel.Device,
                credentials.Value.Username,
                credentials.Value.Password,
                string.IsNullOrWhiteSpace(GatewayAddress) ? null : GatewayAddress.Trim());

            if (!gatewayResult.Succeeded)
            {
                StatusText = $"IPv4 aplicada, gateway rechazado: {gatewayResult.Message}";
                return;
            }

            StatusText = interfaceResult.RebootNeeded
                ? "Cambios aceptados. La cámara indicó que requiere reinicio."
                : "Cambios aceptados. Pulse ACTUALIZAR para comprobar la nueva configuración.";
        }
        catch (Exception ex)
        {
            StatusText = $"Error al aplicar red: {ex.Message}";
        }
        finally
        {
            IsApplying = false;
        }
    }

    [RelayCommand]
    private void Close() => RequestClose?.Invoke(this, EventArgs.Empty);

    private async Task<(string Username, string Password)?> GetCredentialsAsync()
    {
        // La edición siempre requiere una credencial guardada explícitamente.
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
