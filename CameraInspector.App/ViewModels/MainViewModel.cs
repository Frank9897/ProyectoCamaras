using System.Collections.ObjectModel;
using CameraInspector.Core.Interfaces;
using CameraInspector.Core.Models;
using CameraInspector.Video;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CameraInspector.App.ViewModels;

/// <summary>
/// ViewModel de la pantalla principal.
/// Coordina descubrimiento, identificación ONVIF, inventario, credenciales seguras,
/// streams, diagnóstico, historial y reproducción.
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly INetworkInterfaceService _interfaceService;
    private readonly INetworkScanner _scanner;
    private readonly IManufacturerResolver _manufacturerResolver;
    private readonly IOnvifDeviceService _onvifDeviceService;
    private readonly IStreamUriResolver _streamUriResolver;
    private readonly ICameraDiagnosticService _diagnosticService;
    private readonly ICameraInventoryStore _inventoryStore;
    private readonly IDiagnosticHistoryStore _diagnosticHistoryStore;
    private readonly ICredentialStore _credentialStore;
    private readonly ICameraCredentialStore _cameraCredentialStore;
    private readonly IVideoPlayerService _videoPlayerService;

    /// <summary>Dispositivos descubiertos durante el escaneo actual.</summary>
    public ObservableCollection<DeviceViewModel> Devices { get; } = new();

    /// <summary>Resultados de la última batería de diagnóstico ejecutada.</summary>
    public ObservableCollection<DiagnosticResult> DiagnosticResults { get; } = new();

    /// <summary>Últimos registros históricos de la cámara seleccionada.</summary>
    public ObservableCollection<DiagnosticHistoryItem> DiagnosticHistory { get; } = new();

    [ObservableProperty]
    private string _statusText = "Listo para escanear.";

    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private bool _isDiagnosing;

    [ObservableProperty]
    private NetworkInterfaceInfo? _selectedInterface;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GetMainStreamUriCommand))]
    [NotifyCanExecuteChangedFor(nameof(GetSubStreamUriCommand))]
    [NotifyCanExecuteChangedFor(nameof(RunDiagnosticsCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveCredentialsCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteCredentialsCommand))]
    private DeviceViewModel? _selectedDevice;

    [ObservableProperty]
    private CameraStreamInfo? _resolvedMainStream;

    [ObservableProperty]
    private CameraStreamInfo? _resolvedSubStream;

    [ObservableProperty]
    private bool _hasSavedCredentials;

    [ObservableProperty]
    private string? _savedCredentialUsername;

    [ObservableProperty]
    private DateTimeOffset? _savedCredentialLastVerifiedAt;

    [ObservableProperty]
    private bool _useSavedCredentials = true;

    public ObservableCollection<NetworkInterfaceInfo> AvailableInterfaces { get; } = new();

    public MainViewModel(
        INetworkInterfaceService interfaceService,
        INetworkScanner scanner,
        IManufacturerResolver manufacturerResolver,
        IOnvifDeviceService onvifDeviceService,
        IStreamUriResolver streamUriResolver,
        ICameraDiagnosticService diagnosticService,
        ICameraInventoryStore inventoryStore,
        IDiagnosticHistoryStore diagnosticHistoryStore,
        ICredentialStore credentialStore,
        ICameraCredentialStore cameraCredentialStore,
        IVideoPlayerService videoPlayerService)
    {
        // _interfaceService enumera las interfaces de red disponibles para el escaneo.
        _interfaceService = interfaceService;
        // _scanner ejecuta Ping, ARP y WS-Discovery.
        _scanner = scanner;
        // _manufacturerResolver combina evidencias de OUI, HTTP y ONVIF.
        _manufacturerResolver = manufacturerResolver;
        // _onvifDeviceService consulta identidad y capacidades ONVIF.
        _onvifDeviceService = onvifDeviceService;
        // _streamUriResolver resuelve los streams ONVIF.
        _streamUriResolver = streamUriResolver;
        // _diagnosticService ejecuta la batería de diagnóstico.
        _diagnosticService = diagnosticService;
        // _inventoryStore mantiene el inventario persistente de cámaras.
        _inventoryStore = inventoryStore;
        // _diagnosticHistoryStore guarda y consulta los resultados históricos.
        _diagnosticHistoryStore = diagnosticHistoryStore;
        // _credentialStore administra el secreto real en Windows Credential Manager.
        _credentialStore = credentialStore;
        // _cameraCredentialStore relaciona una cámara SQLite con una referencia segura.
        _cameraCredentialStore = cameraCredentialStore;
        // _videoPlayerService controla la reproducción RTSP.
        _videoPlayerService = videoPlayerService;

        foreach (var nic in _interfaceService.GetActiveInterfaces())
            AvailableInterfaces.Add(nic);

        SelectedInterface = AvailableInterfaces.FirstOrDefault();
    }

    partial void OnSelectedDeviceChanged(DeviceViewModel? value)
    {
        // Detenemos el video de la cámara anterior para evitar dos reproducciones simultáneas.
        _videoPlayerService.Stop();

        ResolvedMainStream = null;
        ResolvedSubStream = null;
        DiagnosticResults.Clear();
        DiagnosticHistory.Clear();

        // Restablecemos el estado persistente antes de cargar el de la nueva selección.
        HasSavedCredentials = false;
        SavedCredentialUsername = null;
        SavedCredentialLastVerifiedAt = null;

        if (value is not null)
            _ = LoadSelectedDeviceStateAsync(value);
    }

    /// <summary>
    /// Carga de forma asíncrona la información persistente de seguridad e historial de la cámara seleccionada.
    /// </summary>
    private async Task LoadSelectedDeviceStateAsync(DeviceViewModel viewModel)
    {
        if (viewModel.CameraId is not int cameraId)
            return;

        try
        {
            // credentialInfo contiene únicamente metadatos; la contraseña sigue en Credential Manager.
            var credentialInfo = await _cameraCredentialStore.GetAsync(cameraId);
            if (credentialInfo is not null)
            {
                HasSavedCredentials = true;
                SavedCredentialUsername = credentialInfo.Username;
                SavedCredentialLastVerifiedAt = credentialInfo.LastVerifiedAt;
            }

            // history contiene las últimas pruebas y alimenta la pestaña Historial.
            var history = await _diagnosticHistoryStore.GetRecentAsync(cameraId, 100);
            foreach (var item in history)
                DiagnosticHistory.Add(item);
        }
        catch (Exception ex)
        {
            StatusText = $"No se pudo cargar la información persistente: {ex.Message}";
        }
    }

    [RelayCommand(CanExecute = nameof(CanScan))]
    private async Task ScanAsync(CancellationToken cancellationToken)
    {
        if (SelectedInterface is null)
        {
            StatusText = "No hay ninguna interfaz de red activa detectada.";
            return;
        }

        IsScanning = true;
        Devices.Clear();
        DiagnosticResults.Clear();
        DiagnosticHistory.Clear();
        ResolvedMainStream = null;
        ResolvedSubStream = null;
        _videoPlayerService.Stop();
        StatusText = $"Escaneando {SelectedInterface}...";

        try
        {
            await foreach (var progress in _scanner.ScanAsync(SelectedInterface, cancellationToken: cancellationToken))
            {
                if (progress.NewlyFound is not null)
                {
                    // device es el objeto técnico que se irá completando progresivamente.
                    var device = progress.NewlyFound;
                    // vm representa el dispositivo en la interfaz WPF.
                    var vm = new DeviceViewModel(device);
                    Devices.Add(vm);
                    _ = ResolveDeviceAsync(device, vm, cancellationToken);
                }

                StatusText = $"Escaneando... {progress.Scanned} dispositivos de {progress.Total} IPs candidatas";
            }

            StatusText = $"Escaneo completo: {Devices.Count} dispositivos encontrados.";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Escaneo cancelado.";
        }
        finally
        {
            IsScanning = false;
        }
    }

    private bool CanScan() => !IsScanning && !IsDiagnosing;

    private async Task ResolveDeviceAsync(
        DiscoveredDevice device,
        DeviceViewModel viewModel,
        CancellationToken cancellationToken)
    {
        try
        {
            await _manufacturerResolver.ResolveAsync(device, cancellationToken);
            viewModel.Refresh();

            if (!device.OnvifSupported)
                return;

            var info = await _onvifDeviceService.GetDeviceInformationAsync(
                device, null, null, cancellationToken);

            if (info is not null)
            {
                // Los datos ONVIF tienen prioridad porque provienen directamente del dispositivo.
                device.Manufacturer = info.Manufacturer ?? device.Manufacturer;
                device.Model = info.Model ?? device.Model;
                device.FirmwareVersion = info.FirmwareVersion ?? device.FirmwareVersion;
                device.SerialNumber = info.SerialNumber ?? device.SerialNumber;
                viewModel.Refresh();
            }

            var capabilities = await _onvifDeviceService.GetCapabilitiesAsync(
                device, null, null, cancellationToken);

            if (capabilities is null)
                return;

            device.OnvifDeviceServiceXAddr = capabilities.DeviceServiceXAddr ?? device.OnvifDeviceServiceXAddr;
            device.OnvifMediaServiceXAddr = capabilities.MediaServiceXAddr;
            device.OnvifImagingServiceXAddr = capabilities.ImagingServiceXAddr;
            device.OnvifPtzServiceXAddr = capabilities.PtzServiceXAddr;
            device.OnvifEventsServiceXAddr = capabilities.EventsServiceXAddr;
            device.OnvifSupported = capabilities.HasMediaService || device.OnvifSupported;
            viewModel.Refresh();

            if (device.OnvifSupported)
            {
                // cameraId es la identidad persistente usada por credenciales e historial.
                var cameraId = await _inventoryStore.UpsertAsync(device, cancellationToken);
                viewModel.SetCameraId(cameraId);
            }
        }
        catch (OperationCanceledException)
        {
            // Cancelación normal al detener el escaneo.
        }
        catch
        {
            // El enriquecimiento es complementario: una excepción no elimina el dispositivo ya descubierto.
        }
    }

    /// <summary>
    /// Obtiene las credenciales para una operación.
    /// Si UseSavedCredentials está activo y existe una credencial, el secreto se recupera de Windows Credential Manager.
    /// Si no, se solicita al técnico mediante el diálogo.
    /// </summary>
    private async Task<CredentialSession?> GetCredentialsAsync()
    {
        if (SelectedDevice is null)
            return null;

        if (UseSavedCredentials && SelectedDevice.CameraId is int savedCameraId)
        {
            // savedInfo contiene el enlace entre la cámara y Credential Manager.
            var savedInfo = await _cameraCredentialStore.GetAsync(savedCameraId);
            if (savedInfo is not null)
            {
                // storedCredential contiene el secreto únicamente en memoria durante esta operación.
                var storedCredential = await _credentialStore.GetAsync(savedInfo.CredentialRef);
                if (storedCredential is not null)
                {
                    return new CredentialSession(
                        storedCredential.Username,
                        storedCredential.Password,
                        savedInfo.CredentialRef);
                }

                StatusText = "La credencial guardada ya no existe en Windows Credential Manager.";
            }
        }

        // initialUsername evita obligar al técnico a volver a escribir el usuario almacenado.
        var dialog = new CredentialsDialog(SavedCredentialUsername)
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };

        if (dialog.ShowDialog() != true)
            return null;

        Guid? credentialRef = null;

        if (dialog.SaveCredential && SelectedDevice.CameraId is int cameraId)
        {
            // credentialRef apunta al secreto seguro y nunca contiene la contraseña.
            credentialRef = await _credentialStore.SaveAsync(
                dialog.Username,
                dialog.Password);

            await _cameraCredentialStore.SaveAsync(
                cameraId,
                dialog.Username,
                credentialRef.Value);

            HasSavedCredentials = true;
            SavedCredentialUsername = dialog.Username;
            SavedCredentialLastVerifiedAt = null;
        }

        return new CredentialSession(dialog.Username, dialog.Password, credentialRef);
    }

    [RelayCommand(CanExecute = nameof(CanManageCredentials))]
    private async Task SaveCredentialsAsync()
    {
        if (SelectedDevice?.CameraId is not int cameraId)
            return;

        var dialog = new CredentialsDialog(SavedCredentialUsername)
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };

        if (dialog.ShowDialog() != true)
            return;

        try
        {
            // newCredentialRef es una referencia nueva al secreto que reemplazará la anterior.
            var newCredentialRef = await _credentialStore.SaveAsync(
                dialog.Username,
                dialog.Password);

            var previousCredential = await _cameraCredentialStore.GetAsync(cameraId);
            await _cameraCredentialStore.SaveAsync(
                cameraId,
                dialog.Username,
                newCredentialRef);

            // oldCredentialRef se elimina para evitar secretos abandonados en el almacén seguro.
            if (previousCredential is not null && previousCredential.CredentialRef != newCredentialRef)
                await _credentialStore.DeleteAsync(previousCredential.CredentialRef);

            HasSavedCredentials = true;
            SavedCredentialUsername = dialog.Username;
            SavedCredentialLastVerifiedAt = null;
            StatusText = "Credenciales guardadas de forma segura.";
        }
        catch (Exception ex)
        {
            StatusText = $"No se pudieron guardar las credenciales: {ex.Message}";
        }
    }

    [RelayCommand(CanExecute = nameof(CanManageCredentials))]
    private async Task DeleteCredentialsAsync()
    {
        if (SelectedDevice?.CameraId is not int cameraId)
            return;

        if (System.Windows.MessageBox.Show(
                "¿Desea eliminar las credenciales guardadas para esta cámara?",
                "Camera Inspector — Credenciales",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Warning) != System.Windows.MessageBoxResult.Yes)
            return;

        try
        {
            // savedInfo permite eliminar también el secreto correspondiente de Credential Manager.
            var savedInfo = await _cameraCredentialStore.GetAsync(cameraId);
            await _cameraCredentialStore.DeleteAsync(cameraId);

            if (savedInfo is not null)
                await _credentialStore.DeleteAsync(savedInfo.CredentialRef);

            HasSavedCredentials = false;
            SavedCredentialUsername = null;
            SavedCredentialLastVerifiedAt = null;
            StatusText = "Credenciales eliminadas.";
        }
        catch (Exception ex)
        {
            StatusText = $"No se pudieron eliminar las credenciales: {ex.Message}";
        }
    }

    private bool CanManageCredentials() => SelectedDevice?.CameraId is not null && !IsScanning && !IsDiagnosing;

    [RelayCommand(CanExecute = nameof(CanResolveStream))]
    private async Task GetMainStreamUriAsync()
    {
        if (SelectedDevice is null)
            return;

        var credentials = await GetCredentialsAsync();
        if (credentials is null)
            return;

        try
        {
            StatusText = $"Resolviendo stream principal de {SelectedDevice.IpAddress}...";

            var result = await _streamUriResolver.GetMainStreamUriAsync(
                SelectedDevice.Device,
                credentials.Username,
                credentials.Password);

            if (result is null)
            {
                StatusText = "No se pudo resolver el stream principal.";
                ShowStreamError(SelectedDevice.IpAddress, "principal");
                return;
            }

            ResolvedMainStream = result;
            _videoPlayerService.Play(result, credentials.Username, credentials.Password);
            await MarkCredentialVerifiedAsync(credentials);
            StatusText = $"Main Stream: {result.Resolution} · {result.Encoding ?? "Codec desconocido"} · {result.FrameRate?.ToString() ?? "?"} FPS";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Resolución del stream principal cancelada.";
        }
        catch (Exception ex)
        {
            StatusText = $"No se pudo reproducir el stream principal: {ex.Message}";
        }
    }

    [RelayCommand(CanExecute = nameof(CanResolveStream))]
    private async Task GetSubStreamUriAsync()
    {
        if (SelectedDevice is null)
            return;

        var credentials = await GetCredentialsAsync();
        if (credentials is null)
            return;

        try
        {
            StatusText = $"Resolviendo substream de {SelectedDevice.IpAddress}...";

            var result = await _streamUriResolver.GetSubStreamUriAsync(
                SelectedDevice.Device,
                credentials.Username,
                credentials.Password);

            if (result is null)
            {
                StatusText = "No se pudo resolver el substream.";
                ShowStreamError(SelectedDevice.IpAddress, "secundario");
                return;
            }

            ResolvedSubStream = result;
            _videoPlayerService.Play(result, credentials.Username, credentials.Password);
            await MarkCredentialVerifiedAsync(credentials);
            StatusText = $"Substream: {result.Resolution} · {result.Encoding ?? "Codec desconocido"} · {result.FrameRate?.ToString() ?? "?"} FPS";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Resolución del substream cancelada.";
        }
        catch (Exception ex)
        {
            StatusText = $"No se pudo reproducir el substream: {ex.Message}";
        }
    }

    [RelayCommand]
    private void StopVideo()
    {
        // El servicio encapsula el cierre seguro del MediaPlayer.
        _videoPlayerService.Stop();
        StatusText = "Reproducción detenida.";
    }

    [RelayCommand(CanExecute = nameof(CanRunDiagnostics))]
    private async Task RunDiagnosticsAsync()
    {
        if (SelectedDevice is null)
            return;

        var credentials = await GetCredentialsAsync();
        if (credentials is null)
            return;

        IsDiagnosing = true;
        DiagnosticResults.Clear();
        StatusText = $"Ejecutando diagnóstico sobre {SelectedDevice.IpAddress}...";

        try
        {
            // results contiene todas las pruebas ejecutadas en paralelo.
            var results = await _diagnosticService.RunAsync(
                SelectedDevice.Device,
                credentials.Username,
                credentials.Password);

            foreach (var result in results)
                DiagnosticResults.Add(result);

            if (SelectedDevice.CameraId is int cameraId)
            {
                // Guardamos las pruebas para consultarlas nuevamente después de cerrar la aplicación.
                await _diagnosticHistoryStore.SaveAsync(cameraId, results);
                await RefreshHistoryAsync(cameraId);
            }

            var successCount = results.Count(result => result.Success);
            var supportedCount = results.Count(result => !result.NotSupported);

            if (successCount > 0)
                await MarkCredentialVerifiedAsync(credentials);

            StatusText = $"Diagnóstico terminado: {successCount}/{supportedCount} pruebas correctas.";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Diagnóstico cancelado.";
        }
        catch (Exception ex)
        {
            StatusText = $"Error durante diagnóstico: {ex.Message}";
        }
        finally
        {
            IsDiagnosing = false;
        }
    }

    /// <summary>Actualiza manualmente el historial visible de la cámara seleccionada.</summary>
    [RelayCommand(CanExecute = nameof(CanRefreshHistory))]
    private async Task RefreshHistoryAsync()
    {
        if (SelectedDevice?.CameraId is not int cameraId)
            return;

        DiagnosticHistory.Clear();

        // history contiene como máximo 100 registros para mantener la UI ligera.
        var history = await _diagnosticHistoryStore.GetRecentAsync(cameraId, 100);
        foreach (var item in history)
            DiagnosticHistory.Add(item);

        StatusText = $"Historial actualizado: {DiagnosticHistory.Count} registros.";
    }

    private bool CanRefreshHistory() => SelectedDevice?.CameraId is not null && !IsScanning && !IsDiagnosing;

    private async Task MarkCredentialVerifiedAsync(CredentialSession credentials)
    {
        if (SelectedDevice?.CameraId is not int cameraId || credentials.CredentialRef is not Guid)
            return;

        // verifiedAt representa el instante UTC en el que una operación confirmó la credencial.
        var verifiedAt = DateTimeOffset.UtcNow;
        await _cameraCredentialStore.MarkVerifiedAsync(cameraId, verifiedAt);
        SavedCredentialLastVerifiedAt = verifiedAt;
    }

    private bool CanResolveStream() => SelectedDevice is not null && !IsScanning && !IsDiagnosing;
    private bool CanRunDiagnostics() => SelectedDevice is not null && !IsScanning && !IsDiagnosing;

    private static void ShowStreamError(string ipAddress, string streamType)
    {
        System.Windows.MessageBox.Show(
            $"No se pudo obtener el stream {streamType} de {ipAddress}.\n\n" +
            "Verifique las credenciales, que el dispositivo exponga Media Service ONVIF y que exista al menos un perfil de video.",
            "Camera Inspector — Stream ONVIF",
            System.Windows.MessageBoxButton.OK,
            System.Windows.MessageBoxImage.Warning);
    }

    /// <summary>Credenciales que viven en memoria únicamente durante la operación solicitada.</summary>
    private sealed record CredentialSession(
        string Username,
        string Password,
        Guid? CredentialRef);
}
