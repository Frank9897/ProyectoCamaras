using System.Collections.ObjectModel;
using CameraInspector.Core.Interfaces;
using CameraInspector.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CameraInspector.App.ViewModels;

/// <summary>
/// ViewModel de la pantalla "Escanear".
/// Coordina el descubrimiento de red y la actualización progresiva de la información técnica
/// de cada dispositivo sin bloquear la interfaz WPF.
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
        // _interfaceService permite enumerar las interfaces de red locales que pueden utilizarse para escanear.
        _interfaceService = interfaceService;

        // _scanner contiene el pipeline de Ping, ARP y WS-Discovery.
        _scanner = scanner;

        // _manufacturerResolver ejecuta los detectores de fabricante y protocolos registrados.
        _manufacturerResolver = manufacturerResolver;

        // _onvifDeviceService consulta identidad y capacidades directamente mediante Device Service.
        _onvifDeviceService = onvifDeviceService;

        // _streamUriResolver obtiene posteriormente las URLs RTSP desde Media Service.
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
                    // device representa el estado técnico mutable del dispositivo que acaba de aparecer.
                    var device = progress.NewlyFound;

                    // vm es la representación de presentación enlazada al DataGrid de WPF.
                    var vm = new DeviceViewModel(device);
                    Devices.Add(vm);

                    // Cada dispositivo se resuelve de manera independiente para que una cámara lenta
                    // no bloquee la aparición de las demás cámaras en la tabla.
                    _ = ResolveDeviceAsync(device, vm, cancellationToken);
                }

                // progress mantiene al técnico informado del avance del barrido de red.
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
    /// Completa la información de un dispositivo después del descubrimiento inicial.
    /// Primero ejecuta los detectores generales y, cuando existe ONVIF, consulta identidad
    /// directamente al Device Service para obtener la información declarada por el dispositivo.
    /// </summary>
    private async Task ResolveDeviceAsync(
        DiscoveredDevice device,
        DeviceViewModel viewModel,
        CancellationToken cancellationToken)
    {
        try
        {
            // La resolución de fabricante combina OUI, HTTP y ONVIF y asigna los datos con mayor confianza.
            await _manufacturerResolver.ResolveAsync(device, cancellationToken);
            viewModel.Refresh();

            // Solo consultamos GetDeviceInformation cuando tenemos evidencia de que el dispositivo expone ONVIF.
            if (!device.OnvifSupported)
                return;

            // info contiene los datos de identidad enviados directamente por el Device Service ONVIF.
            var info = await _onvifDeviceService.GetDeviceInformationAsync(
                device,
                username: null,
                password: null,
                cancellationToken);

            if (info is null)
                return;

            // Solo reemplazamos un dato existente cuando ONVIF realmente proporciona un valor.
            // Esto evita perder información obtenida por otro detector si algún firmware omite un campo.
            device.Manufacturer = info.Manufacturer ?? device.Manufacturer;
            device.Model = info.Model ?? device.Model;
            device.FirmwareVersion = info.FirmwareVersion ?? device.FirmwareVersion;
            device.SerialNumber = info.SerialNumber ?? device.SerialNumber;

            // Refresh notifica a WPF para que el DataGrid y el panel de detalle vuelvan a leer los valores.
            viewModel.Refresh();
        }
        catch (OperationCanceledException)
        {
            // La cancelación forma parte del flujo normal cuando el técnico detiene el escaneo.
        }
        catch
        {
            // Un fallo de identificación no invalida el dispositivo ya descubierto.
            // El equipo permanece visible con la información que sí pudimos obtener.
        }
    }

    /// <summary>
    /// Validación manual de la Capa 5: pide credenciales por diálogo y muestra la URL RTSP real
    /// obtenida mediante ONVIF Media Service. La reproducción pertenece a la futura Capa 7.
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
