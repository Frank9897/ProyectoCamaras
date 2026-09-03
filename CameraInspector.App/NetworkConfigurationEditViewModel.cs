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
/// La interfaz prioriza lectura, validación y confirmación antes de cualquier escritura.
/// </summary>
public sealed partial class NetworkConfigurationEditViewModel : ObservableObject
{
    private readonly DeviceViewModel _deviceViewModel;
    private readonly IOnvifDeviceService _onvifDeviceService;
    private readonly ICredentialStore _credentialStore;
    private readonly ICameraCredentialStore _cameraCredentialStore;
    private readonly IOnvifNetworkConfigurationService _writer;

    [ObservableProperty] private string _statusText = "Listo. Consulte la configuración actual antes de modificarla.";
    [ObservableProperty] private OnvifNetworkConfiguration? _configuration;
    [ObservableProperty] private OnvifNetworkInterfaceInfo? _selectedInterface;
    [ObservableProperty] private bool _useDhcp;
    [ObservableProperty] private string _ipv4Address = string.Empty;
    [ObservableProperty] private string _prefixLength = "24";
    [ObservableProperty] private string _gatewayAddress = string.Empty;
    [ObservableProperty] private string _hostname = string.Empty;
    [ObservableProperty] private bool _isApplying;
    [ObservableProperty] private bool _isSystemActionRunning;
    [ObservableProperty] private bool _isStatusError;
    [ObservableProperty] private bool _hasUnsavedChanges;
    [ObservableProperty] private string _validationMessage = "";

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
        _deviceViewModel = deviceViewModel;
        _onvifDeviceService = onvifDeviceService;
        _credentialStore = credentialStore;
        _cameraCredentialStore = cameraCredentialStore;
        _writer = new OnvifNetworkConfigurationService();
    }

    partial void OnSelectedInterfaceChanged(OnvifNetworkInterfaceInfo? value)
    {
        if (value is null)
            return;

        UseDhcp = value.IPv4Dhcp == true;
        Ipv4Address = value.IPv4Address ?? string.Empty;
        PrefixLength = (value.IPv4PrefixLength ?? 24).ToString();
        HasUnsavedChanges = false;
        ValidationMessage = string.Empty;
    }

    partial void OnUseDhcpChanged(bool value) => HasUnsavedChanges = true;
    partial void OnIpv4AddressChanged(string value) => HasUnsavedChanges = true;
    partial void OnPrefixLengthChanged(string value) => HasUnsavedChanges = true;
    partial void OnGatewayAddressChanged(string value) => HasUnsavedChanges = true;
    partial void OnHostnameChanged(string value) => HasUnsavedChanges = true;

    private void SetStatus(string message, bool error = false)
    {
        StatusText = message;
        IsStatusError = error;
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        if (IsApplying || IsSystemActionRunning)
            return;

        SetStatus("Consultando configuración actual de la cámara...");
        ValidationMessage = string.Empty;

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
                SetStatus("ALERTA: la cámara no devolvió información de red ONVIF. Compruebe credenciales, Device Service y compatibilidad ONVIF.", true);
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
            HasUnsavedChanges = false;
            SetStatus($"OK: configuración leída. {Interfaces.Count} interfaz(es), {Protocols.Count} protocolo(s), {Gateways.Count} gateway(s).");
        }
        catch (Exception ex)
        {
            SetStatus($"ALERTA: error al consultar la configuración de red: {ex.Message}", true);
        }
    }

    [RelayCommand]
    private void ValidateNetwork()
    {
        if (SelectedInterface is null)
        {
            ValidationMessage = "Seleccione una interfaz de red.";
            SetStatus("ALERTA: no hay una interfaz seleccionada.", true);
            return;
        }

        if (!UseDhcp)
        {
            if (!IPAddress.TryParse(Ipv4Address.Trim(), out var ipv4)
                || ipv4.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
            {
                ValidationMessage = "La IPv4 no es válida.";
                SetStatus("ALERTA: la dirección IPv4 indicada no es válida.", true);
                return;
            }

            if (!int.TryParse(PrefixLength.Trim(), out var prefix) || prefix < 1 || prefix > 32)
            {
                ValidationMessage = "El prefijo debe estar entre 1 y 32.";
                SetStatus("ALERTA: el prefijo CIDR debe estar entre 1 y 32.", true);
                return;
            }

            if (IPAddress.Parse(Ipv4Address).Equals(IPAddress.Broadcast) || IPAddress.Parse(Ipv4Address).Equals(IPAddress.Any))
            {
                ValidationMessage = "No utilice 0.0.0.0 ni 255.255.255.255 como dirección de cámara.";
                SetStatus("ALERTA: la IPv4 seleccionada no puede utilizarse para la cámara.", true);
                return;
            }
        }

        if (!string.IsNullOrWhiteSpace(GatewayAddress)
            && (!IPAddress.TryParse(GatewayAddress.Trim(), out var gateway)
                || gateway.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork))
        {
            ValidationMessage = "El gateway no es una IPv4 válida.";
            SetStatus("ALERTA: el gateway IPv4 indicado no es válido.", true);
            return;
        }

        ValidationMessage = UseDhcp
            ? "VALIDACIÓN OK: la interfaz quedará configurada por DHCP."
            : "VALIDACIÓN OK: IPv4, prefijo y gateway tienen formato válido.";
        SetStatus("OK: configuración de red validada. Todavía no se realizaron cambios en la cámara.");
    }

    [RelayCommand]
    private async Task ApplyAsync()
    {
        if (SelectedInterface is null || IsApplying || IsSystemActionRunning)
        {
            SetStatus("ALERTA: seleccione una interfaz válida.", true);
            return;
        }

        ValidateNetwork();
        if (IsStatusError)
            return;

        if (!HasUnsavedChanges)
        {
            SetStatus("No hay cambios pendientes para aplicar.");
            return;
        }

        var currentIp = SelectedInterface.IPv4Address ?? "(sin IP)";
        var targetIp = UseDhcp ? "DHCP" : Ipv4Address.Trim();
        var currentPrefix = SelectedInterface.IPv4PrefixLength?.ToString() ?? "?";
        var targetPrefix = UseDhcp ? "DHCP" : PrefixLength.Trim();
        var currentGateway = Gateways.FirstOrDefault() ?? "(sin gateway)";
        var targetGateway = string.IsNullOrWhiteSpace(GatewayAddress) ? "(sin gateway)" : GatewayAddress.Trim();

        var result = MessageBox.Show(
            $"RESUMEN DEL CAMBIO\n\n" +
            $"Interfaz: {SelectedInterface.Token}\n\n" +
            $"IP:       {currentIp}  →  {targetIp}\n" +
            $"Prefijo:  {currentPrefix}   →  {targetPrefix}\n" +
            $"Gateway:  {currentGateway}  →  {targetGateway}\n\n" +
            "IMPORTANTE: cambiar la IP puede cortar inmediatamente la conexión con la cámara.\n" +
            "Aplique estos cambios solamente si conoce la nueva dirección y dispone de una ruta para volver a acceder.\n\n" +
            "¿Desea aplicar la configuración?",
            "Camera Inspector — Confirmar cambio de red",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
        {
            SetStatus("Cambio cancelado. La configuración de la cámara no fue modificada.");
            return;
        }

        try
        {
            IsApplying = true;
            SetStatus("Aplicando configuración de red... no cierre esta ventana.");

            var credentials = await GetCredentialsAsync();
            if (credentials is null)
                return;

            // El gateway se configura primero. Así evitamos perder el endpoint ONVIF
            // antes de haber enviado el gateway final cuando la cámara cambia de IP.
            SetStatus("Paso 1/2: aplicando gateway...");
            var gatewayResult = await _writer.SetDefaultGatewayAsync(
                _deviceViewModel.Device,
                credentials.Value.Username,
                credentials.Value.Password,
                string.IsNullOrWhiteSpace(GatewayAddress) ? null : GatewayAddress.Trim());

            if (!gatewayResult.Succeeded)
            {
                SetStatus($"ALERTA: no se pudo aplicar el gateway. La IP de la cámara no fue modificada. Motivo: {gatewayResult.Message}", true);
                return;
            }

            SetStatus("Paso 2/2: aplicando IPv4...");
            int? prefix = null;
            if (!UseDhcp && int.TryParse(PrefixLength.Trim(), out var prefixValue))
                prefix = prefixValue;

            var interfaceResult = await _writer.SetIPv4Async(
                _deviceViewModel.Device,
                credentials.Value.Username,
                credentials.Value.Password,
                SelectedInterface.Token,
                UseDhcp,
                UseDhcp ? null : Ipv4Address.Trim(),
                prefix);

            if (!interfaceResult.Succeeded)
            {
                SetStatus($"ALERTA: el gateway fue aplicado, pero la IPv4 fue rechazada. Revise la configuración antes de continuar. Motivo: {interfaceResult.Message}", true);
                return;
            }

            HasUnsavedChanges = false;
            SetStatus(interfaceResult.RebootNeeded
                ? "OK: la cámara aceptó la nueva red y solicita reinicio. La conexión puede interrumpirse ahora."
                : "OK: cambios de red aceptados. Pulse ACTUALIZAR para confirmar el estado de la cámara.");
        }
        catch (Exception ex)
        {
            SetStatus($"ALERTA: error durante la aplicación de red: {ex.Message}", true);
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
        if (_deviceViewModel.CameraId is not int cameraId)
        {
            SetStatus("ALERTA: la cámara todavía no tiene identidad persistente para asociar credenciales.", true);
            return null;
        }

        var savedInfo = await _cameraCredentialStore.GetAsync(cameraId);
        if (savedInfo is null)
        {
            SetStatus("ALERTA: no hay credenciales guardadas para esta cámara. Configure las credenciales desde VIDEO antes de administrar la red.", true);
            return null;
        }

        var stored = await _credentialStore.GetAsync(savedInfo.CredentialRef);
        if (stored is null)
        {
            SetStatus("ALERTA: la referencia de credenciales existe, pero el secreto ya no está disponible en Windows Credential Manager.", true);
            return null;
        }

        return (stored.Username, stored.Password);
    }
}
