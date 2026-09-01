using System.Collections.ObjectModel;
using CameraInspector.Core.Interfaces;
using CameraInspector.Core.Models;
using CameraInspector.Video;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CameraInspector.App.ViewModels;

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

    public ObservableCollection<DeviceViewModel> Devices { get; } = new();
    private readonly List<DeviceViewModel> _allDiscoveredDevices = new();
    public ObservableCollection<DiagnosticResult> DiagnosticResults { get; } = new();
    public ObservableCollection<DiagnosticHistoryItem> DiagnosticHistory { get; } = new();

    [ObservableProperty] private string _statusText = "Listo para escanear.";
    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] private bool _isDiagnosing;
    [ObservableProperty] private NetworkInterfaceInfo? _selectedInterface;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GetMainStreamUriCommand))]
    [NotifyCanExecuteChangedFor(nameof(GetSubStreamUriCommand))]
    [NotifyCanExecuteChangedFor(nameof(RunDiagnosticsCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveCredentialsCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteCredentialsCommand))]
    private DeviceViewModel? _selectedDevice;

    [ObservableProperty] private CameraStreamInfo? _resolvedMainStream;
    [ObservableProperty] private CameraStreamInfo? _resolvedSubStream;
    [ObservableProperty] private bool _hasSavedCredentials;
    [ObservableProperty] private string? _savedCredentialUsername;
    [ObservableProperty] private DateTimeOffset? _savedCredentialLastVerifiedAt;
    [ObservableProperty] private bool _useSavedCredentials = true;

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
        _interfaceService = interfaceService;
        _scanner = scanner;
        _manufacturerResolver = manufacturerResolver;
        _onvifDeviceService = onvifDeviceService;
        _streamUriResolver = streamUriResolver;
        _diagnosticService = diagnosticService;
        _inventoryStore = inventoryStore;
        _diagnosticHistoryStore = diagnosticHistoryStore;
        _credentialStore = credentialStore;
        _cameraCredentialStore = cameraCredentialStore;
        _videoPlayerService = videoPlayerService;

        foreach (var nic in _interfaceService.GetActiveInterfaces())
            AvailableInterfaces.Add(nic);
        SelectedInterface = AvailableInterfaces.FirstOrDefault();
    }

    partial void OnSelectedDeviceChanged(DeviceViewModel? value)
    {
        _videoPlayerService.Stop();
        ResolvedMainStream = null;
        ResolvedSubStream = null;
        DiagnosticResults.Clear();
        DiagnosticHistory.Clear();
        HasSavedCredentials = false;
        SavedCredentialUsername = null;
        SavedCredentialLastVerifiedAt = null;
        if (value is not null)
            _ = LoadSelectedDeviceStateAsync(value);
    }

    private async Task LoadSelectedDeviceStateAsync(DeviceViewModel viewModel)
    {
        if (viewModel.CameraId is not int cameraId) return;
        try
        {
            var credentialInfo = await _cameraCredentialStore.GetAsync(cameraId);
            if (credentialInfo is not null)
            {
                HasSavedCredentials = true;
                SavedCredentialUsername = credentialInfo.Username;
                SavedCredentialLastVerifiedAt = credentialInfo.LastVerifiedAt;
            }
            var history = await _diagnosticHistoryStore.GetRecentAsync(cameraId, 100);
            foreach (var item in history) DiagnosticHistory.Add(item);
        }
        catch (Exception ex) { StatusText = $"No se pudo cargar la información persistente: {ex.Message}"; }
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
        _allDiscoveredDevices.Clear();
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
                    var device = progress.NewlyFound;
                    var vm = new DeviceViewModel(device);
                    _allDiscoveredDevices.Add(vm);
                    await ResolveDeviceAsync(device, vm, cancellationToken);
                }
                StatusText = $"Escaneando... {progress.Scanned} de {progress.Total} · Cámaras visibles: {Devices.Count}";
            }
            StatusText = $"Escaneo completo: {Devices.Count} cámaras/dispositivos de imagen encontrados.";
        }
        catch (OperationCanceledException) { StatusText = "Escaneo cancelado."; }
        finally { IsScanning = false; }
    }

    private bool CanScan() => !IsScanning && !IsDiagnosing;

    private async Task ResolveDeviceAsync(DiscoveredDevice device, DeviceViewModel viewModel, CancellationToken cancellationToken)
    {
        try
        {
            await _manufacturerResolver.ResolveAsync(device, cancellationToken);
            viewModel.Refresh();

            // ONVIF es una evidencia, no el requisito. CameraEvidence permite mostrar cámaras propietarias,
            // legacy, mDNS/SSDP o descubiertas por fingerprint especializado.
            var isCameraCandidate = device.CameraEvidence || device.OnvifSupported || device.RtspSupported;
            if (!isCameraCandidate) return;

            if (!Devices.Contains(viewModel)) Devices.Add(viewModel);

            if (device.OnvifSupported)
            {
                var info = await _onvifDeviceService.GetDeviceInformationAsync(device, null, null, cancellationToken);
                if (info is not null)
                {
                    device.Manufacturer = info.Manufacturer ?? device.Manufacturer;
                    device.Model = info.Model ?? device.Model;
                    device.FirmwareVersion = info.FirmwareVersion ?? device.FirmwareVersion;
                    device.SerialNumber = info.SerialNumber ?? device.SerialNumber;
                    viewModel.Refresh();
                }

                var capabilities = await _onvifDeviceService.GetCapabilitiesAsync(device, null, null, cancellationToken);
                if (capabilities is not null)
                {
                    device.OnvifDeviceServiceXAddr = capabilities.DeviceServiceXAddr ?? device.OnvifDeviceServiceXAddr;
                    device.OnvifMediaServiceXAddr = capabilities.MediaServiceXAddr;
                    device.OnvifImagingServiceXAddr = capabilities.ImagingServiceXAddr;
                    device.OnvifPtzServiceXAddr = capabilities.PtzServiceXAddr;
                    device.OnvifEventsServiceXAddr = capabilities.EventsServiceXAddr;
                    device.OnvifSupported = capabilities.HasMediaService || device.OnvifSupported;
                    viewModel.Refresh();
                }
            }

            if (isCameraCandidate)
            {
                var cameraId = await _inventoryStore.UpsertAsync(device, cancellationToken);
                viewModel.SetCameraId(cameraId);
            }
        }
        catch (OperationCanceledException) { }
        catch { }
    }

    private async Task<CredentialSession?> GetCredentialsAsync()
    {
        if (SelectedDevice is null) return null;
        if (UseSavedCredentials && SelectedDevice.CameraId is int savedCameraId)
        {
            var savedInfo = await _cameraCredentialStore.GetAsync(savedCameraId);
            if (savedInfo is not null)
            {
                var storedCredential = await _credentialStore.GetAsync(savedInfo.CredentialRef);
                if (storedCredential is not null)
                    return new CredentialSession(storedCredential.Username, storedCredential.Password, savedInfo.CredentialRef);
            }
        }

        var dialog = new CredentialsDialog(SavedCredentialUsername) { Owner = System.Windows.Application.Current?.MainWindow };
        if (dialog.ShowDialog() != true) return null;
        Guid? credentialRef = null;
        if (dialog.SaveCredential && SelectedDevice.CameraId is int cameraId)
        {
            credentialRef = await _credentialStore.SaveAsync(dialog.Username, dialog.Password);
            await _cameraCredentialStore.SaveAsync(cameraId, dialog.Username, credentialRef.Value);
            HasSavedCredentials = true;
            SavedCredentialUsername = dialog.Username;
            SavedCredentialLastVerifiedAt = null;
        }
        return new CredentialSession(dialog.Username, dialog.Password, credentialRef);
    }

    [RelayCommand(CanExecute = nameof(CanManageCredentials))]
    private async Task SaveCredentialsAsync()
    {
        if (SelectedDevice?.CameraId is not int cameraId) return;
        var dialog = new CredentialsDialog(SavedCredentialUsername) { Owner = System.Windows.Application.Current?.MainWindow };
        if (dialog.ShowDialog() != true) return;
        try
        {
            var newCredentialRef = await _credentialStore.SaveAsync(dialog.Username, dialog.Password);
            var previousCredential = await _cameraCredentialStore.GetAsync(cameraId);
            await _cameraCredentialStore.SaveAsync(cameraId, dialog.Username, newCredentialRef);
            if (previousCredential is not null && previousCredential.CredentialRef != newCredentialRef)
                await _credentialStore.DeleteAsync(previousCredential.CredentialRef);
            HasSavedCredentials = true;
            SavedCredentialUsername = dialog.Username;
            SavedCredentialLastVerifiedAt = null;
            StatusText = "Credenciales guardadas de forma segura.";
        }
        catch (Exception ex) { StatusText = $"No se pudieron guardar las credenciales: {ex.Message}"; }
    }

    [RelayCommand(CanExecute = nameof(CanManageCredentials))]
    private async Task DeleteCredentialsAsync()
    {
        if (SelectedDevice?.CameraId is not int cameraId) return;
        if (System.Windows.MessageBox.Show("¿Desea eliminar las credenciales guardadas para esta cámara?", "Camera Inspector — Credenciales", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning) != System.Windows.MessageBoxResult.Yes) return;
        try
        {
            var savedInfo = await _cameraCredentialStore.GetAsync(cameraId);
            await _cameraCredentialStore.DeleteAsync(cameraId);
            if (savedInfo is not null) await _credentialStore.DeleteAsync(savedInfo.CredentialRef);
            HasSavedCredentials = false;
            SavedCredentialUsername = null;
            SavedCredentialLastVerifiedAt = null;
            StatusText = "Credenciales eliminadas.";
        }
        catch (Exception ex) { StatusText = $"No se pudieron eliminar las credenciales: {ex.Message}"; }
    }

    private bool CanManageCredentials() => SelectedDevice?.CameraId is not null && !IsScanning && !IsDiagnosing;

    [RelayCommand(CanExecute = nameof(CanResolveStream))]
    private async Task GetMainStreamUriAsync()
    {
        if (SelectedDevice is null) return;
        var credentials = await GetCredentialsAsync();
        if (credentials is null) return;
        try
        {
            StatusText = $"Resolviendo stream principal de {SelectedDevice.IpAddress}...";
            var result = await _streamUriResolver.GetMainStreamUriAsync(SelectedDevice.Device, credentials.Username, credentials.Password);
            if (result is null) { StatusText = "No se pudo resolver el stream principal."; ShowStreamError(SelectedDevice.IpAddress, "principal"); return; }
            ResolvedMainStream = result;
            _videoPlayerService.Play(result, credentials.Username, credentials.Password);
            await MarkCredentialVerifiedAsync(credentials);
            StatusText = $"Main Stream: {result.Resolution} · {result.Encoding ?? "Codec desconocido"} · {result.FrameRate?.ToString() ?? "?"} FPS";
        }
        catch (OperationCanceledException) { StatusText = "Resolución del stream principal cancelada."; }
        catch (Exception ex) { StatusText = $"No se pudo reproducir el stream principal: {ex.Message}"; }
    }

    [RelayCommand(CanExecute = nameof(CanResolveStream))]
    private async Task GetSubStreamUriAsync()
    {
        if (SelectedDevice is null) return;
        var credentials = await GetCredentialsAsync();
        if (credentials is null) return;
        try
        {
            StatusText = $"Resolviendo substream de {SelectedDevice.IpAddress}...";
            var result = await _streamUriResolver.GetSubStreamUriAsync(SelectedDevice.Device, credentials.Username, credentials.Password);
            if (result is null) { StatusText = "No se pudo resolver el substream."; ShowStreamError(SelectedDevice.IpAddress, "secundario"); return; }
            ResolvedSubStream = result;
            _videoPlayerService.Play(result, credentials.Username, credentials.Password);
            await MarkCredentialVerifiedAsync(credentials);
            StatusText = $"Substream: {result.Resolution} · {result.Encoding ?? "Codec desconocido"} · {result.FrameRate?.ToString() ?? "?"} FPS";
        }
        catch (OperationCanceledException) { StatusText = "Resolución del substream cancelada."; }
        catch (Exception ex) { StatusText = $"No se pudo reproducir el substream: {ex.Message}"; }
    }

    [RelayCommand]
    private void StopVideo() { _videoPlayerService.Stop(); StatusText = "Reproducción detenida."; }

    [RelayCommand(CanExecute = nameof(CanRunDiagnostics))]
    private async Task RunDiagnosticsAsync()
    {
        if (SelectedDevice is null) return;
        var credentials = await GetCredentialsAsync();
        if (credentials is null) return;
        IsDiagnosing = true;
        DiagnosticResults.Clear();
        StatusText = $"Ejecutando diagnóstico sobre {SelectedDevice.IpAddress}...";
        try
        {
            var results = await _diagnosticService.RunAsync(SelectedDevice.Device, credentials.Username, credentials.Password);
            foreach (var result in results) DiagnosticResults.Add(result);
            if (SelectedDevice.CameraId is int cameraId)
            {
                await _diagnosticHistoryStore.SaveAsync(cameraId, results);
                await RefreshHistoryAsync(cameraId);
            }
            var successCount = results.Count(result => result.Success);
            var supportedCount = results.Count(result => !result.NotSupported);
            if (successCount > 0) await MarkCredentialVerifiedAsync(credentials);
            StatusText = $"Diagnóstico terminado: {successCount}/{supportedCount} pruebas correctas.";
        }
        catch (OperationCanceledException) { StatusText = "Diagnóstico cancelado."; }
        catch (Exception ex) { StatusText = $"Error durante diagnóstico: {ex.Message}"; }
        finally { IsDiagnosing = false; }
    }

    [RelayCommand(CanExecute = nameof(CanRefreshHistory))]
    private async Task RefreshHistoryAsync()
    {
        if (SelectedDevice?.CameraId is not int cameraId) return;
        await RefreshHistoryAsync(cameraId);
        StatusText = $"Historial actualizado: {DiagnosticHistory.Count} registros.";
    }

    private bool CanRefreshHistory() => SelectedDevice?.CameraId is not null && !IsScanning && !IsDiagnosing;

    private async Task MarkCredentialVerifiedAsync(CredentialSession credentials)
    {
        if (SelectedDevice?.CameraId is not int cameraId || credentials.CredentialRef is not Guid) return;
        var verifiedAt = DateTimeOffset.UtcNow;
        await _cameraCredentialStore.MarkVerifiedAsync(cameraId, verifiedAt);
        SavedCredentialLastVerifiedAt = verifiedAt;
    }

    private bool CanResolveStream() => SelectedDevice is not null && !IsScanning && !IsDiagnosing;
    private bool CanRunDiagnostics() => SelectedDevice is not null && !IsScanning && !IsDiagnosing;

    private static void ShowStreamError(string ipAddress, string streamType)
    {
        System.Windows.MessageBox.Show(
            $"No se pudo obtener el stream {streamType} de {ipAddress}.\n\nVerifique las credenciales, la disponibilidad del servicio de vídeo y que la cámara exponga un protocolo compatible.",
            "Camera Inspector — Stream",
            System.Windows.MessageBoxButton.OK,
            System.Windows.MessageBoxImage.Warning);
    }

    private sealed record CredentialSession(string Username, string Password, Guid? CredentialRef);
}
