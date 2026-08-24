using System.Collections.ObjectModel;
using CameraInspector.Core.Interfaces;
using CameraInspector.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CameraInspector.App.ViewModels;

/// <summary>
/// ViewModel de la pantalla principal.
/// Coordina descubrimiento, identificación ONVIF, streams y diagnóstico rápido.
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly INetworkInterfaceService _interfaceService;
    private readonly INetworkScanner _scanner;
    private readonly IManufacturerResolver _manufacturerResolver;
    private readonly IOnvifDeviceService _onvifDeviceService;
    private readonly IStreamUriResolver _streamUriResolver;
    private readonly ICameraDiagnosticService _diagnosticService;

    public ObservableCollection<DeviceViewModel> Devices { get; } = new();

    /// <summary>Resultados de la última batería de diagnóstico ejecutada.</summary>
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
        ICameraDiagnosticService diagnosticService)
    {
        // _interfaceService enumera las interfaces de red que pueden utilizarse para el escaneo.
        _interfaceService = interfaceService;

        // _scanner ejecuta el pipeline de Ping, ARP y WS-Discovery.
        _scanner = scanner;

        // _manufacturerResolver combina las evidencias de identificación disponibles.
        _manufacturerResolver = manufacturerResolver;

        // _onvifDeviceService consulta identidad y capacidades oficiales del dispositivo.
        _onvifDeviceService = onvifDeviceService;

        // _streamUriResolver resuelve Main/Sub Stream mediante Media Service ONVIF.
        _streamUriResolver = streamUriResolver;

        // _diagnosticService ejecuta las pruebas técnicas de conectividad y protocolos.
        _diagnosticService = diagnosticService;

        foreach (var nic in _interfaceService.GetActiveInterfaces())
            AvailableInterfaces.Add(nic);

        SelectedInterface = AvailableInterfaces.FirstOrDefault();
    }

    partial void OnSelectedDeviceChanged(DeviceViewModel? value)
    {
        // Al cambiar de cámara, eliminamos resultados de streams y diagnóstico de la selección anterior.
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
        StatusText = $"Escaneando {SelectedInterface}...";

        try
        {
            await foreach (var progress in _scanner.ScanAsync(SelectedInterface, cancellationToken: cancellationToken))
            {
                if (progress.NewlyFound is not null)
                {
                    // device contiene el estado técnico mutable que compartirán los servicios.
                    var device = progress.NewlyFound;

                    // vm adapta el modelo técnico al binding de WPF.
                    var vm = new DeviceViewModel(device);
                    Devices.Add(vm);

                    // La identificación se ejecuta en segundo plano para mantener fluida la tabla.
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
            // La primera fase usa detectores generales para identificar fabricante y protocolos.
            await _manufacturerResolver.ResolveAsync(device, cancellationToken);
            viewModel.Refresh();

            if (!device.OnvifSupported)
                return;

            // info representa la identidad entregada directamente por ONVIF.
            var info = await _onvifDeviceService.GetDeviceInformationAsync(
                device,
                username: null,
                password: null,
                cancellationToken);

            if (info is not null)
            {
                device.Manufacturer = info.Manufacturer ?? device.Manufacturer;
                device.Model = info.Model ?? device.Model;
                device.FirmwareVersion = info.FirmwareVersion ?? device.FirmwareVersion;
                device.SerialNumber = info.SerialNumber ?? device.SerialNumber;
                viewModel.Refresh();
            }

            // capabilities contiene los XAddr reales publicados por el firmware.
            var capabilities = await _onvifDeviceService.GetCapabilitiesAsync(
                device,
                username: null,
                password: null,
                cancellationToken);

            if (capabilities is null)
                return;

            device.OnvifDeviceServiceXAddr = capabilities.DeviceServiceXAddr ?? device.OnvifDeviceServiceXAddr;
            device.OnvifMediaServiceXAddr = capabilities.MediaServiceXAddr;
            device.OnvifImagingServiceXAddr = capabilities.ImagingServiceXAddr;
            device.OnvifPtzServiceXAddr = capabilities.PtzServiceXAddr;
            device.OnvifEventsServiceXAddr = capabilities.EventsServiceXAddr;

            if (capabilities.HasMediaService)
                device.OnvifSupported = true;

            viewModel.Refresh();
        }
        catch (OperationCanceledException)
        {
            // Cancelación normal al detener el escaneo.
        }
        catch
        {
            // La identificación fallida no elimina la cámara de los resultados.
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

            // result contiene la URI RTSP y todos los parámetros del perfil seleccionado.
            var result = await _streamUriResolver.GetMainStreamUriAsync(
                SelectedDevice.Device,
                dialog.Username,
                dialog.Password);

            if (result is null)
            {
                StatusText = "No se pudo resolver el stream principal.";
                ShowStreamError(SelectedDevice.IpAddress, "principal");
                return;
            }

            ResolvedMainStream = result;
            StatusText = $"Main Stream: {result.Resolution} · {result.Encoding ?? "Codec desconocido"} · {result.FrameRate?.ToString() ?? "?"} FPS";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Resolución del stream principal cancelada.";
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

            // result contiene la URI RTSP y parámetros del perfil secundario.
            var result = await _streamUriResolver.GetSubStreamUriAsync(
                SelectedDevice.Device,
                dialog.Username,
                dialog.Password);

            if (result is null)
            {
                StatusText = "No se pudo resolver el substream.";
                ShowStreamError(SelectedDevice.IpAddress, "secundario");
                return;
            }

            ResolvedSubStream = result;
            StatusText = $"Substream: {result.Resolution} · {result.Encoding ?? "Codec desconocido"} · {result.FrameRate?.ToString() ?? "?"} FPS";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Resolución del substream cancelada.";
        }
    }

    /// <summary>
    /// Ejecuta la batería de diagnóstico de la cámara seleccionada.
    /// </summary>
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
            // results contiene una instantánea con todas las pruebas ejecutadas en paralelo.
            var results = await _diagnosticService.RunAsync(
                SelectedDevice.Device,
                dialog.Username,
                dialog.Password);

            foreach (var result in results)
                DiagnosticResults.Add(result);

            // successCount representa cuántas pruebas finalizaron correctamente.
            var successCount = results.Count(result => result.Success);

            // supportedCount cuenta las pruebas cuyo resultado fue "no soportado" y no deben tratarse como fallos.
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
