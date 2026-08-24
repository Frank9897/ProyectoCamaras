using System.Collections.ObjectModel;
using CameraInspector.Core.Interfaces;
using CameraInspector.Core.Models;
using CameraInspector.Video;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CameraInspector.App.ViewModels;

/// <summary>
/// ViewModel de la pantalla principal.
/// Coordina descubrimiento, identificación ONVIF, inventario, streams, diagnóstico y reproducción.
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
    private readonly IVideoPlayerService _videoPlayerService;

    public ObservableCollection<DeviceViewModel> Devices { get; } = new();
    public ObservableCollection<DiagnosticResult> DiagnosticResults { get; } = new();

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
    private DeviceViewModel? _selectedDevice;

    [ObservableProperty]
    private CameraStreamInfo? _resolvedMainStream;

    [ObservableProperty]
    private CameraStreamInfo? _resolvedSubStream;

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
        IVideoPlayerService videoPlayerService)
    {
        // _interfaceService enumera las interfaces de red disponibles.
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
        // _inventoryStore convierte una cámara identificada en inventario persistente.
        _inventoryStore = inventoryStore;
        // _diagnosticHistoryStore guarda las pruebas después de una ejecución.
        _diagnosticHistoryStore = diagnosticHistoryStore;
        // _videoPlayerService controla la reproducción RTSP.
        _videoPlayerService = videoPlayerService;

        foreach (var nic in _interfaceService.GetActiveInterfaces())
            AvailableInterfaces.Add(nic);

        SelectedInterface = AvailableInterfaces.FirstOrDefault();
    }

    partial void OnSelectedDeviceChanged(DeviceViewModel? value)
    {
        // Detenemos el video de la cámara anterior para evitar reproducir dos streams simultáneamente.
        _videoPlayerService.Stop();
        ResolvedMainStream = null;
        ResolvedSubStream = null;
        DiagnosticResults.Clear();
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

            // Solo persistimos cuando tenemos evidencia de que realmente estamos ante una cámara ONVIF.
            // Esto evita llenar SQLite con PCs, routers y otros hosts que respondan al ping.
            if (device.OnvifSupported)
            {
                // cameraId es la clave persistente que permitirá asociar futuros diagnósticos e historial.
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

    [RelayCommand(CanExecute = nameof(CanResolveStream))]
    private async Task GetMainStreamUriAsync()
    {
        if (SelectedDevice is null)
            return;

        var dialog = new CredentialsDialog();
        if (dialog.ShowDialog() != true)
            return;

        try
        {
            StatusText = $"Resolviendo stream principal de {SelectedDevice.IpAddress}...";

            var result = await _streamUriResolver.GetMainStreamUriAsync(
                SelectedDevice.Device, dialog.Username, dialog.Password);

            if (result is null)
            {
                StatusText = "No se pudo resolver el stream principal.";
                ShowStreamError(SelectedDevice.IpAddress, "principal");
                return;
            }

            ResolvedMainStream = result;
            _videoPlayerService.Play(result, dialog.Username, dialog.Password);
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

        var dialog = new CredentialsDialog();
        if (dialog.ShowDialog() != true)
            return;

        try
        {
            StatusText = $"Resolviendo substream de {SelectedDevice.IpAddress}...";

            var result = await _streamUriResolver.GetSubStreamUriAsync(
                SelectedDevice.Device, dialog.Username, dialog.Password);

            if (result is null)
            {
                StatusText = "No se pudo resolver el substream.";
                ShowStreamError(SelectedDevice.IpAddress, "secundario");
                return;
            }

            ResolvedSubStream = result;
            _videoPlayerService.Play(result, dialog.Username, dialog.Password);
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
        _videoPlayerService.Stop();
        StatusText = "Reproducción detenida.";
    }

    [RelayCommand(CanExecute = nameof(CanRunDiagnostics))]
    private async Task RunDiagnosticsAsync()
    {
        if (SelectedDevice is null)
            return;

        var dialog = new CredentialsDialog();
        if (dialog.ShowDialog() != true)
            return;

        IsDiagnosing = true;
        DiagnosticResults.Clear();
        StatusText = $"Ejecutando diagnóstico sobre {SelectedDevice.IpAddress}...";

        try
        {
            // results contiene todas las pruebas ejecutadas en paralelo.
            var results = await _diagnosticService.RunAsync(
                SelectedDevice.Device, dialog.Username, dialog.Password);

            foreach (var result in results)
                DiagnosticResults.Add(result);

            // Si la cámara ya está en inventario, guardamos el historial inmediatamente.
            if (SelectedDevice.CameraId is int cameraId)
                await _diagnosticHistoryStore.SaveAsync(cameraId, results);

            var successCount = results.Count(result => result.Success);
            var supportedCount = results.Count(result => !result.NotSupported);

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
}
