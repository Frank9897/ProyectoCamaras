using System.Collections.ObjectModel;
using System.Net;
using System.Windows;
using CameraInspector.App.ViewModels;
using CameraInspector.Core.Interfaces;
using CameraInspector.Core.Models;
using CameraInspector.Network.OnvifMedia;
using CameraInspector.Network.Providers.Dahua;
using CameraInspector.Network.Providers.Hikvision;
using CameraInspector.Network.Providers.Vivotek;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CameraInspector.App;

/// <summary>
/// ViewModel específico para edición controlada de red.
/// La interfaz prioriza lectura, validación y confirmación antes de cualquier escritura.
///
/// REDISEÑO (pedido: "que funcione para cualquier cámara, vieja o nueva, ONVIF o no"):
/// Antes este ViewModel solo sabía de dos caminos: ONVIF, o VIVOTEK CGI como único legacy
/// hardcodeado. Ahora la elección de método se basa en:
///   1) Si la cámara fue marcada como compatible ONVIF (Device.OnvifSupported), se intenta
///      ONVIF primero, sea cual sea el fabricante.
///   2) Si ONVIF no responde (o de entrada no está soportado), se detecta el fabricante por
///      nombre/modelo/evidencia y se usa su CGI/ISAPI legacy correspondiente:
///        - VIVOTEK  -> CGI getparam/setparam.cgi (familia IP71xx: 7122, 7133, 7134, etc.)
///        - DAHUA    -> CGI configManager.cgi (incluye clones OEM tipo Amcrest)
///        - HIKVISION-> ISAPI (XML sobre HTTP GET/PUT)
///   3) Si el fabricante no es ninguno de los anteriores y ONVIF falló, se informa con
///      honestidad que esta versión no tiene un método de configuración de red implementado
///      para ese fabricante puntual, en vez de fallar en silencio o fingir que funcionó.
/// </summary>
public sealed partial class NetworkConfigurationEditViewModel : ObservableObject
{
    private readonly DeviceViewModel _deviceViewModel;
    private readonly IOnvifDeviceService _onvifDeviceService;
    private readonly ICredentialStore _credentialStore;
    private readonly ICameraCredentialStore _cameraCredentialStore;
    private readonly IOnvifNetworkConfigurationService _writer;
    private readonly VivotekLegacyConfigurationService _vivotekWriter;
    private readonly DahuaLegacyConfigurationService _dahuaWriter;
    private readonly HikvisionIsapiNetworkConfigurationService _hikvisionWriter;

    [ObservableProperty] private string _statusText = "Listo. Consulte la configuración actual antes de modificarla.";
    [ObservableProperty] private OnvifNetworkConfiguration? _configuration;
    [ObservableProperty] private OnvifNetworkInterfaceInfo? _selectedInterface;
    [ObservableProperty] private bool _useDhcp;
    [ObservableProperty] private string _ipv4Address = string.Empty;
    [ObservableProperty] private string _prefixLength = "24";
    [ObservableProperty] private string _gatewayAddress = string.Empty;
    [ObservableProperty] private bool _isApplying;
    [ObservableProperty] private bool _isSystemActionRunning;
    [ObservableProperty] private bool _isStatusError;
    [ObservableProperty] private bool _hasUnsavedChanges;
    [ObservableProperty] private string _validationMessage = string.Empty;

    public DiscoveredDevice Device => _deviceViewModel.Device;
    public string CameraIpAddress => _deviceViewModel.IpAddress;
    public string CameraManufacturer => _deviceViewModel.Manufacturer;
    public string CameraModel => _deviceViewModel.Model;

    public ObservableCollection<OnvifNetworkInterfaceInfo> Interfaces { get; } = new();
    public ObservableCollection<OnvifNetworkProtocolInfo> Protocols { get; } = new();
    public ObservableCollection<string> Gateways { get; } = new();

    // Se recuerda qué camino terminó funcionando en LoadAsync (null = ONVIF, o el par
    // escritor+etiqueta de un fabricante legacy) para que ApplyAsync use exactamente el
    // mismo método y no vuelva a "adivinar" con lógica separada que podría desalinearse.
    private (ILegacyCameraNetworkConfigurationService Writer, string VendorLabel)? _activeLegacyPath;

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
        _vivotekWriter = new VivotekLegacyConfigurationService();
        _dahuaWriter = new DahuaLegacyConfigurationService();
        _hikvisionWriter = new HikvisionIsapiNetworkConfigurationService();
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

    private void SetStatus(string message, bool error = false)
    {
        StatusText = message;
        IsStatusError = error;
    }

    /// <summary>
    /// Detecta, por manufacturer/model/evidencia de descubrimiento, qué escritor legacy
    /// corresponde a este dispositivo. Devuelve null si el fabricante no tiene un
    /// escritor implementado en esta app (no significa que la cámara no tenga forma de
    /// configurarse, solo que esta versión no la implementa todavía).
    /// </summary>
    private (ILegacyCameraNetworkConfigurationService Writer, string VendorLabel)? DetectLegacyWriter()
    {
        var manufacturer = _deviceViewModel.Manufacturer ?? string.Empty;
        var model = _deviceViewModel.Model ?? string.Empty;
        var evidence = string.Join(" ", Device.DetectionEvidence.Select(item => item.Method));

        // VIVOTEK: familia fija IP71xx completa (7122, 7123, 7133, 7134, 7135, 7136...),
        // no solo los dos modelos que estaban hardcodeados originalmente.
        if (manufacturer.Contains("VIVOTEK", StringComparison.OrdinalIgnoreCase)
            || model.Contains("IP71", StringComparison.OrdinalIgnoreCase)
            || evidence.Contains("VIVOTEK", StringComparison.OrdinalIgnoreCase))
            return (_vivotekWriter, "VIVOTEK");

        if (manufacturer.Contains("Dahua", StringComparison.OrdinalIgnoreCase)
            || manufacturer.Contains("Amcrest", StringComparison.OrdinalIgnoreCase)
            || evidence.Contains("Dahua", StringComparison.OrdinalIgnoreCase))
            return (_dahuaWriter, "DAHUA");

        if (manufacturer.Contains("Hikvision", StringComparison.OrdinalIgnoreCase)
            || model.StartsWith("DS-", StringComparison.OrdinalIgnoreCase)
            || evidence.Contains("Hikvision", StringComparison.OrdinalIgnoreCase))
            return (_hikvisionWriter, "HIKVISION");

        return null;
    }

    private void ApplyLoadedConfiguration(OnvifNetworkConfiguration loaded, string source)
    {
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
        SetStatus($"OK: configuración leída mediante {source}. {Interfaces.Count} interfaz(es), {Protocols.Count} protocolo(s), {Gateways.Count} gateway(s).");
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        if (IsApplying || IsSystemActionRunning)
            return;

        SetStatus("Consultando configuración actual de la cámara...");
        ValidationMessage = string.Empty;
        _activeLegacyPath = null;

        try
        {
            var legacy = DetectLegacyWriter();
            var credentials = await GetCredentialsAsync(legacy);
            if (credentials is null)
                return;

            // Si el dispositivo no fue marcado como ONVIF durante el descubrimiento y sí
            // reconocemos su fabricante como uno legacy, vamos directo por ese camino:
            // es más rápido y evita un timeout ONVIF innecesario en cámaras que sabemos
            // de antemano que no lo hablan.
            if (!Device.OnvifSupported && legacy is not null)
            {
                if (await TryLoadLegacyAsync(legacy.Value, credentials.Value))
                    return;
            }

            var loaded = await _onvifDeviceService.GetNetworkConfigurationAsync(
                _deviceViewModel.Device,
                credentials.Value.Username,
                credentials.Value.Password);

            if (loaded is not null)
            {
                _activeLegacyPath = null;
                ApplyLoadedConfiguration(loaded, "ONVIF");
                return;
            }

            // Auto-recuperación: si ONVIF no respondió, probamos el escritor legacy
            // correspondiente (si hay uno detectado) antes de rendirnos. Esto cubre
            // cámaras agregadas manualmente por IP, sin depender de que el descubrimiento
            // haya etiquetado bien el fabricante de antemano.
            if (legacy is not null && await TryLoadLegacyAsync(legacy.Value, credentials.Value))
                return;

            SetStatus(legacy is null
                ? $"ALERTA: la cámara no respondió a ONVIF y su fabricante ({(string.IsNullOrWhiteSpace(CameraManufacturer) ? "desconocido" : CameraManufacturer)}) no tiene un método de configuración de red implementado en esta versión. Soportados actualmente: ONVIF, VIVOTEK, DAHUA e HIKVISION."
                : "ALERTA: la cámara no devolvió información de red. Configure el acceso antes de administrar la red.", true);
        }
        catch (Exception ex)
        {
            SetStatus($"ALERTA: error al consultar la configuración de red: {ex.Message}", true);
        }
    }

    /// <summary>
    /// Intenta leer la red por un escritor legacy puntual, con el mismo mecanismo de
    /// reintento con credenciales administrativas que antes solo existía para VIVOTEK.
    /// Devuelve true y deja la configuración aplicada si tuvo éxito.
    /// </summary>
    private async Task<bool> TryLoadLegacyAsync(
        (ILegacyCameraNetworkConfigurationService Writer, string VendorLabel) legacy,
        (string Username, string Password) credentials)
    {
        var legacyLoaded = await legacy.Writer.GetNetworkConfigurationAsync(
            _deviceViewModel.Device, credentials.Username, credentials.Password);

        if (legacyLoaded is not null)
        {
            _activeLegacyPath = legacy;
            ApplyLoadedConfiguration(legacyLoaded, $"CGI/ISAPI {legacy.VendorLabel} legacy");
            return true;
        }

        // Si se usó una credencial por defecto vacía (solo aplica a VIVOTEK, ver
        // GetCredentialsAsync), recién ahora se solicita una credencial administrativa.
        if (credentials.Username.Equals("root", StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrEmpty(credentials.Password)
            && legacy.VendorLabel == "VIVOTEK")
        {
            SetStatus($"La administración {legacy.VendorLabel} solicita autenticación. Ingrese las credenciales actuales para continuar.");
            var authenticated = await RequestAdministrativeCredentialsAsync("root");
            if (authenticated is not null)
            {
                legacyLoaded = await legacy.Writer.GetNetworkConfigurationAsync(
                    _deviceViewModel.Device, authenticated.Value.Username, authenticated.Value.Password);

                if (legacyLoaded is not null)
                {
                    _activeLegacyPath = legacy;
                    ApplyLoadedConfiguration(legacyLoaded, $"CGI/ISAPI {legacy.VendorLabel} autenticado");
                    return true;
                }
            }
        }

        return false;
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

            if (ipv4.Equals(IPAddress.Broadcast) || ipv4.Equals(IPAddress.Any))
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

        // FIX: esta revalidación se había perdido en una reescritura previa del método.
        // Es la misma validación de ValidateNetwork() (IP/prefijo/gateway bien formados)
        // y debe correr siempre antes de mostrar el diálogo de confirmación de cambios.
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

        var result = ThemedMessageBox.Show(
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

            // Se reutiliza el mismo camino que funcionó al leer (_activeLegacyPath), en vez
            // de volver a detectar el fabricante: si LoadAsync leyó por ONVIF, se escribe por
            // ONVIF; si leyó por un CGI/ISAPI legacy puntual, se escribe por ese mismo.
            var legacy = _activeLegacyPath;
            SetStatus(legacy is not null
                ? $"Aplicando configuración mediante {legacy.Value.VendorLabel} legacy... no cierre esta ventana."
                : "Aplicando configuración de red mediante ONVIF... no cierre esta ventana.");

            var credentials = await GetCredentialsAsync(legacy);
            if (credentials is null)
                return;

            if (legacy is not null)
            {
                await ApplyLegacyAsync(legacy.Value, credentials.Value);
                return;
            }

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
            int? onvifPrefix = null;
            if (!UseDhcp && int.TryParse(PrefixLength.Trim(), out var onvifPrefixValue))
                onvifPrefix = onvifPrefixValue;

            var interfaceResult = await _writer.SetIPv4Async(
                _deviceViewModel.Device,
                credentials.Value.Username,
                credentials.Value.Password,
                SelectedInterface.Token,
                UseDhcp,
                UseDhcp ? null : Ipv4Address.Trim(),
                onvifPrefix);

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

    /// <summary>
    /// Escribe la red usando un escritor legacy puntual (VIVOTEK/DAHUA/HIKVISION), con el
    /// mismo reintento con credenciales administrativas que antes solo existía para VIVOTEK.
    /// </summary>
    private async Task ApplyLegacyAsync(
        (ILegacyCameraNetworkConfigurationService Writer, string VendorLabel) legacy,
        (string Username, string Password) credentials)
    {
        int? prefix = null;
        if (!UseDhcp && int.TryParse(PrefixLength.Trim(), out var prefixValue))
            prefix = prefixValue;

        var legacyResult = await legacy.Writer.SetNetworkAsync(
            _deviceViewModel.Device,
            credentials.Username,
            credentials.Password,
            UseDhcp,
            UseDhcp ? null : Ipv4Address.Trim(),
            prefix,
            string.IsNullOrWhiteSpace(GatewayAddress) ? null : GatewayAddress.Trim());

        if (!legacyResult.Succeeded && legacyResult.Message.Contains("HTTP 401", StringComparison.OrdinalIgnoreCase)
            && credentials.Username.Equals("root", StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrEmpty(credentials.Password)
            && legacy.VendorLabel == "VIVOTEK")
        {
            SetStatus($"La cámara exige autenticación administrativa. Ingrese las credenciales actuales para aplicar la red.");
            var authenticated = await RequestAdministrativeCredentialsAsync("root");
            if (authenticated is not null)
            {
                legacyResult = await legacy.Writer.SetNetworkAsync(
                    _deviceViewModel.Device,
                    authenticated.Value.Username,
                    authenticated.Value.Password,
                    UseDhcp,
                    UseDhcp ? null : Ipv4Address.Trim(),
                    prefix,
                    string.IsNullOrWhiteSpace(GatewayAddress) ? null : GatewayAddress.Trim());
            }
        }

        if (!legacyResult.Succeeded)
        {
            SetStatus($"ALERTA: {legacy.VendorLabel} rechazó el cambio. Motivo: {legacyResult.Message}", true);
            return;
        }

        if (!UseDhcp && !string.IsNullOrWhiteSpace(Ipv4Address))
            _deviceViewModel.IpAddress = Ipv4Address.Trim();

        HasUnsavedChanges = false;
        SetStatus(legacyResult.RebootNeeded
            ? $"OK: {legacy.VendorLabel} aceptó la nueva red. La cámara puede reiniciar o quedar momentáneamente inaccesible en la IP anterior."
            : $"OK: {legacy.VendorLabel} aceptó los cambios de red.");
    }

    [RelayCommand]
    private void Close() => RequestClose?.Invoke(this, EventArgs.Empty);

    private async Task<(string Username, string Password)?> RequestAdministrativeCredentialsAsync(string defaultUsername)
    {
        var dialog = new CredentialsDialog(defaultUsername)
        {
            Owner = Application.Current?.Windows.OfType<Window>().FirstOrDefault(window => window.IsActive)
                     ?? Application.Current?.MainWindow
        };

        if (dialog.ShowDialog() != true)
            return null;

        if (string.IsNullOrWhiteSpace(dialog.Username))
        {
            SetStatus("ALERTA: el usuario administrativo no puede quedar vacío.", true);
            return null;
        }

        return (dialog.Username.Trim(), dialog.Password ?? string.Empty);
    }

    private async Task<(string Username, string Password)?> GetCredentialsAsync(
        (ILegacyCameraNetworkConfigurationService Writer, string VendorLabel)? legacy)
    {
        if (_deviceViewModel.CameraId is int cameraId)
        {
            var savedInfo = await _cameraCredentialStore.GetAsync(cameraId);
            if (savedInfo is not null)
            {
                var stored = await _credentialStore.GetAsync(savedInfo.CredentialRef);
                if (stored is not null)
                    return (stored.Username, stored.Password);
            }
        }

        // Sin credenciales guardadas: se prueba el usuario administrativo por defecto
        // de fábrica del fabricante detectado (DefaultAdminUsername) con contraseña
        // vacía. Es un intento barato y sin riesgo: si la cámara lo rechaza (equipos
        // DAHUA/HIKVISION suelen exigir contraseña desde el primer arranque), el flujo
        // normal de reintento con credenciales administrativas se activa solo. Esto es,
        // además, el "paso libre" que permite configurar por primera vez una cámara que
        // todavía no tiene credenciales propias, incluyendo cargarlas por primera vez.
        if (legacy is not null)
            return (legacy.Value.Writer.DefaultAdminUsername, string.Empty);

        SetStatus("ALERTA: no hay credenciales guardadas para esta cámara. Configure el acceso antes de administrar la red.", true);
        return null;
    }
}
