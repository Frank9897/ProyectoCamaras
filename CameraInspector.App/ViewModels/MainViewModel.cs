using System.Collections.ObjectModel;
using CameraInspector.Core.Interfaces;
using CameraInspector.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CameraInspector.App.ViewModels;

/// <summary>
/// ViewModel de la pantalla "Escanear" (la primera pantalla del mockup).
/// El comando ScanCommand corre el pipeline completo de la Capa 3 de forma asíncrona
/// y va agregando filas a Devices a medida que aparecen — la UI nunca se bloquea.
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly INetworkInterfaceService _interfaceService;
    private readonly INetworkScanner _scanner;
    private readonly IManufacturerResolver _manufacturerResolver;
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
        IStreamUriResolver streamUriResolver)
    {
        _interfaceService = interfaceService;
        _scanner = scanner;
        _manufacturerResolver = manufacturerResolver;
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
                    var vm = new DeviceViewModel(progress.NewlyFound);
                    Devices.Add(vm);

                    // Fire-and-forget deliberado: no bloquea el barrido de red esperando
                    // la resolución de fabricante de CADA dispositivo uno por uno.
                    // El continueWith vuelve al hilo de UI porque no usamos ConfigureAwait(false).
                    _ = ResolveDeviceAsync(progress.NewlyFound, vm, cancellationToken);
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
    /// Corre la Capa 4 (resolución de fabricante) para un dispositivo puntual y refresca
    /// su fila en la tabla cuando termina. Separado de ScanAsync para que cada dispositivo
    /// se resuelva de forma independiente y no serialice el descubrimiento de toda la red.
    /// </summary>
    private async Task ResolveDeviceAsync(DiscoveredDevice device, DeviceViewModel viewModel, CancellationToken cancellationToken)
    {
        try
        {
            await _manufacturerResolver.ResolveAsync(device, cancellationToken);
            viewModel.Refresh();
        }
        catch (OperationCanceledException)
        {
            // El escaneo se canceló mientras se resolvía este dispositivo: no es un error.
        }
    }

    /// <summary>
    /// Validación manual de la Capa 5: pide credenciales por diálogo (provisorio hasta
    /// que exista la Capa 9 / Credential Manager) y muestra la URL RTSP real resuelta
    /// vía ONVIF Media Service. Todavía no reproduce video — eso es la Capa 7.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanResolveStream))]
    private async Task GetStreamUriAsync()
    {
        if (SelectedDevice is null) return;

        var dialog = new CredentialsDialog();
        if (dialog.ShowDialog() != true) return;

        StatusText = $"Consultando Media Service de {SelectedDevice.IpAddress}...";

        var result = await _streamUriResolver.GetMainStreamUriAsync(
            SelectedDevice.Device, dialog.Username, dialog.Password);

        if (result is null)
        {
            System.Windows.MessageBox.Show(
                $"No se pudo obtener la URL de stream para {SelectedDevice.IpAddress}.\n\n" +
                "Puede ser que el dispositivo no soporte Media Service ONVIF, o que las credenciales sean incorrectas.",
                "Camera Inspector — Capa 5", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            StatusText = "No se pudo resolver la URL de stream.";
            return;
        }

        System.Windows.MessageBox.Show(
            $"URL de stream resuelta:\n\n{result.RtspUri}\n\nPerfil: {result.ProfileToken}",
            "Camera Inspector — Capa 5", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
        StatusText = "URL de stream resuelta correctamente.";
    }

    private bool CanResolveStream() => SelectedDevice is not null;
}
