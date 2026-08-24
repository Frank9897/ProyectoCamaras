using System.Collections.ObjectModel;
using CameraInspector.Core.Interfaces;
using CameraInspector.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CameraInspector.App.ViewModels;

/// <summary>
/// ViewModel de la pantalla principal.
/// Coordina descubrimiento, identificación ONVIF y las primeras pruebas de stream.
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly INetworkInterfaceService _interfaceService;
    private readonly INetworkScanner _scanner;
    private readonly IManufacturerResolver _manufacturerResolver;
    private readonly IOnvifDeviceService _onvifDeviceService;
    private readonly IStreamUriResolver _streamUriResolver;

    public ObservableCollection<DeviceViewModel> Devices { get; } = new();

    [ObservableProperty]
    private string _statusText = "Listo para escanear.";

    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private NetworkInterfaceInfo? _selectedInterface;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GetMainStreamUriCommand))]
    [NotifyCanExecuteChangedFor(nameof(GetSubStreamUriCommand))]
    private DeviceViewModel? _selectedDevice;

    /// <summary>
    /// Último stream principal resuelto manualmente para la cámara seleccionada.
    /// </summary>
    [ObservableProperty]
    private CameraStreamInfo? _resolvedMainStream;

    /// <summary>
    /// Último stream secundario resuelto manualmente para la cámara seleccionada.
    /// </summary>
    [ObservableProperty]
    private CameraStreamInfo? _resolvedSubStream;

    public ObservableCollection<NetworkInterfaceInfo> AvailableInterfaces { get; } = new();

    public MainViewModel(
        INetworkInterfaceService interfaceService,
        INetworkScanner scanner,
        IManufacturerResolver manufacturerResolver,
        IOnvifDeviceService onvifDeviceService,
        IStreamUriResolver streamUriResolver)
    {
        // _interfaceService permite enumerar las interfaces de red activas.
        _interfaceService = interfaceService;

        // _scanner ejecuta Ping, ARP y WS-Discovery sin acoplar la UI a la infraestructura de red.
        _scanner = scanner;

        // _manufacturerResolver combina OUI, HTTP y ONVIF para aumentar la confianza de identificación.
        _manufacturerResolver = manufacturerResolver;

        // _onvifDeviceService consulta identidad y capacidades publicadas por el Device Service.
        _onvifDeviceService = onvifDeviceService;

        // _streamUriResolver resuelve las URI RTSP de Main/Sub mediante Media Service ONVIF.
        _streamUriResolver = streamUriResolver;

        foreach (var nic in _interfaceService.GetActiveInterfaces())
            AvailableInterfaces.Add(nic);

        SelectedInterface = AvailableInterfaces.FirstOrDefault();
    }

    partial void OnSelectedDeviceChanged(DeviceViewModel? value)
    {
        // Al seleccionar otra cámara, limpiamos los resultados de streams de la selección anterior.
        ResolvedMainStream = null;
        ResolvedSubStream = null;
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
        ResolvedMainStream = null;
        ResolvedSubStream = null;
        StatusText = $"Escaneando {SelectedInterface}...";

        try
        {
            await foreach (var progress in _scanner.ScanAsync(SelectedInterface, cancellationToken: cancellationToken))
            {
                if (progress.NewlyFound is not null)
                {
                    // device contiene el modelo técnico mutable compartido entre las capas.
                    var device = progress.NewlyFound;

                    // vm representa el mismo dispositivo para los bindings de WPF.
                    var vm = new DeviceViewModel(device);
                    Devices.Add(vm);

                    // La identificación ocurre en segundo plano para que las nuevas IP sigan apareciendo inmediatamente.
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

    private bool CanScan() => !IsScanning;

    /// <summary>
    /// Completa los datos técnicos del dispositivo después de su descubrimiento inicial.
    /// </summary>
    private async Task ResolveDeviceAsync(
        DiscoveredDevice device,
        DeviceViewModel viewModel,
        CancellationToken cancellationToken)
    {
        try
        {
            // Ejecutamos primero los detectores generales porque pueden encontrar ONVIF, HTTP, RTSP y fabricante.
            await _manufacturerResolver.ResolveAsync(device, cancellationToken);
            viewModel.Refresh();

            // Sin ONVIF confirmado no podemos obtener sus servicios mediante SOAP/Device Service.
            if (!device.OnvifSupported)
                return;

            // info contiene la identidad reportada directamente por la cámara mediante GetDeviceInformation.
            var info = await _onvifDeviceService.GetDeviceInformationAsync(
                device,
                username: null,
                password: null,
                cancellationToken);

            if (info is not null)
            {
                // ONVIF tiene prioridad cuando devuelve un valor, pero no destruye datos obtenidos por otros detectores.
                device.Manufacturer = info.Manufacturer ?? device.Manufacturer;
                device.Model = info.Model ?? device.Model;
                device.FirmwareVersion = info.FirmwareVersion ?? device.FirmwareVersion;
                device.SerialNumber = info.SerialNumber ?? device.SerialNumber;
                viewModel.Refresh();
            }

            // capabilities contiene los XAddr reales que el dispositivo publica para sus distintos servicios.
            var capabilities = await _onvifDeviceService.GetCapabilitiesAsync(
                device,
                username: null,
                password: null,
                cancellationToken);

            if (capabilities is null)
                return;

            // Conservamos los XAddr para que Media/PTZ/Events nunca tengan que adivinar rutas.
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
            // La cancelación forma parte del flujo normal cuando el usuario detiene un escaneo.
        }
        catch
        {
            // Un fallo de enriquecimiento no elimina el dispositivo: conservamos los datos que sí pudieron obtenerse.
        }
    }

    /// <summary>
    /// Pide credenciales y resuelve el stream principal de mayor resolución.
    /// </summary>
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

            // result contiene la URI RTSP y los parámetros técnicos del perfil principal.
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
            StatusText = $"Stream principal resuelto: {result.Resolution} · {result.Encoding ?? "Codec desconocido"} · {result.FrameRate?.ToString() ?? "?"} FPS";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Resolución del stream principal cancelada.";
        }
    }

    /// <summary>
    /// Pide credenciales y resuelve el stream secundario de menor resolución.
    /// </summary>
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

            // result contiene la URI RTSP y los parámetros técnicos del perfil secundario.
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
            StatusText = $"Substream resuelto: {result.Resolution} · {result.Encoding ?? "Codec desconocido"} · {result.FrameRate?.ToString() ?? "?"} FPS";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Resolución del substream cancelada.";
        }
    }

    /// <summary>CanExecute común para los comandos Main/Sub.</summary>
    private bool CanResolveStream() => SelectedDevice is not null;

    /// <summary>
    /// Muestra un mensaje consistente cuando una consulta Media Service no devuelve stream.
    /// </summary>
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
