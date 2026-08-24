using System.Collections.ObjectModel;
using CameraInspector.Core.Interfaces;
using CameraInspector.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CameraInspector.App.ViewModels;

/// <summary>
/// ViewModel de la pantalla "Escanear".
/// Coordina descubrimiento, identificación y enriquecimiento técnico progresivo de cada dispositivo.
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
    [NotifyCanExecuteChangedFor(nameof(GetStreamUriCommand))]
    private DeviceViewModel? _selectedDevice;

    public ObservableCollection<NetworkInterfaceInfo> AvailableInterfaces { get; } = new();

    public MainViewModel(
        INetworkInterfaceService interfaceService,
        INetworkScanner scanner,
        IManufacturerResolver manufacturerResolver,
        IOnvifDeviceService onvifDeviceService,
        IStreamUriResolver streamUriResolver)
    {
        // _interfaceService permite enumerar las interfaces de red activas que pueden escanearse.
        _interfaceService = interfaceService;

        // _scanner ejecuta el descubrimiento físico/lógico: Ping, ARP y WS-Discovery.
        _scanner = scanner;

        // _manufacturerResolver combina evidencias de OUI, HTTP y ONVIF.
        _manufacturerResolver = manufacturerResolver;

        // _onvifDeviceService consulta identidad y capacidades oficiales del Device Service.
        _onvifDeviceService = onvifDeviceService;

        // _streamUriResolver obtiene las URLs RTSP cuando el técnico pida visualizar un stream.
        _streamUriResolver = streamUriResolver;

        foreach (var nic in _interfaceService.GetActiveInterfaces())
            AvailableInterfaces.Add(nic);

        SelectedInterface = AvailableInterfaces.FirstOrDefault();
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
        StatusText = $"Escaneando {SelectedInterface}...";

        try
        {
            await foreach (var progress in _scanner.ScanAsync(SelectedInterface, cancellationToken: cancellationToken))
            {
                if (progress.NewlyFound is not null)
                {
                    // device contiene el modelo técnico que será enriquecido progresivamente por los servicios.
                    var device = progress.NewlyFound;

                    // vm representa el mismo dispositivo dentro de la interfaz WPF.
                    var vm = new DeviceViewModel(device);
                    Devices.Add(vm);

                    // La resolución se ejecuta de forma independiente para que un dispositivo lento
                    // no bloquee la aparición inmediata de los siguientes resultados.
                    _ = ResolveDeviceAsync(device, vm, cancellationToken);
                }

                StatusText = $"Escaneando... {progress.Scanned} respondieron de {progress.Total} IPs candidatas";
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
    /// Enriquece progresivamente un dispositivo con evidencias generales y ONVIF.
    /// </summary>
    private async Task ResolveDeviceAsync(
        DiscoveredDevice device,
        DeviceViewModel viewModel,
        CancellationToken cancellationToken)
    {
        try
        {
            // Primero ejecutamos todos los detectores generales porque pueden descubrir ONVIF,
            // HTTP, RTSP y datos básicos de fabricante sin credenciales.
            await _manufacturerResolver.ResolveAsync(device, cancellationToken);
            viewModel.Refresh();

            // Si no existe evidencia ONVIF, no tiene sentido consultar Device Service.
            if (!device.OnvifSupported)
                return;

            // info representa la identidad declarada directamente por el dispositivo ONVIF.
            var info = await _onvifDeviceService.GetDeviceInformationAsync(
                device,
                username: null,
                password: null,
                cancellationToken);

            if (info is not null)
            {
                // ONVIF tiene prioridad porque es información declarada por el propio dispositivo.
                device.Manufacturer = info.Manufacturer ?? device.Manufacturer;
                device.Model = info.Model ?? device.Model;
                device.FirmwareVersion = info.FirmwareVersion ?? device.FirmwareVersion;
                device.SerialNumber = info.SerialNumber ?? device.SerialNumber;

                // Refrescamos inmediatamente la UI para que el técnico vea la identidad resuelta.
                viewModel.Refresh();
            }

            // capabilities contiene los endpoints reales que el firmware publica para sus servicios.
            var capabilities = await _onvifDeviceService.GetCapabilitiesAsync(
                device,
                username: null,
                password: null,
                cancellationToken);

            if (capabilities is null)
                return;

            // Guardamos cada XAddr para que las siguientes capas no tengan que adivinar rutas ONVIF.
            device.OnvifDeviceServiceXAddr = capabilities.DeviceServiceXAddr ?? device.OnvifDeviceServiceXAddr;
            device.OnvifMediaServiceXAddr = capabilities.MediaServiceXAddr;
            device.OnvifImagingServiceXAddr = capabilities.ImagingServiceXAddr;
            device.OnvifPtzServiceXAddr = capabilities.PtzServiceXAddr;
            device.OnvifEventsServiceXAddr = capabilities.EventsServiceXAddr;

            // Si Media existe, sabemos que el dispositivo ofrece el servicio que necesitamos
            // para descubrir sus perfiles y streams de vídeo ONVIF.
            if (capabilities.HasMediaService)
                device.OnvifSupported = true;

            viewModel.Refresh();
        }
        catch (OperationCanceledException)
        {
            // La cancelación ocurre normalmente cuando el técnico detiene el escaneo.
        }
        catch
        {
            // Un fallo de enriquecimiento no elimina el dispositivo de la tabla.
            // Conservamos toda la información que haya podido obtenerse antes del error.
        }
    }

    /// <summary>
    /// Resolución manual del stream principal. Aquí todavía utilizamos credenciales temporales;
    /// posteriormente serán reemplazadas por el Credential Manager de Windows.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanResolveStream))]
    private async Task GetStreamUriAsync()
    {
        if (SelectedDevice is null)
            return;

        var dialog = new CredentialsDialog();
        if (dialog.ShowDialog() != true)
            return;

        StatusText = $"Consultando Media Service de {SelectedDevice.IpAddress}...";

        var result = await _streamUriResolver.GetMainStreamUriAsync(
            SelectedDevice.Device,
            dialog.Username,
            dialog.Password);

        if (result is null)
        {
            System.Windows.MessageBox.Show(
                $"No se pudo obtener la URL de stream para {SelectedDevice.IpAddress}.\n\n" +
                "Puede ser que el dispositivo no soporte Media Service ONVIF, que el Media XAddr no esté disponible o que las credenciales sean incorrectas.",
                "Camera Inspector — Capa 5",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);

            StatusText = "No se pudo resolver la URL de stream.";
            return;
        }

        System.Windows.MessageBox.Show(
            $"URL de stream resuelta:\n\n{result.RtspUri}\n\nPerfil: {result.ProfileToken}",
            "Camera Inspector — Capa 5",
            System.Windows.MessageBoxButton.OK,
            System.Windows.MessageBoxImage.Information);

        StatusText = "URL de stream resuelta correctamente.";
    }

    private bool CanResolveStream() => SelectedDevice is not null;
}
