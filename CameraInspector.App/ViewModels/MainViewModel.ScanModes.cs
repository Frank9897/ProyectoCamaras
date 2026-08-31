using CameraInspector.Core.Interfaces;
using CameraInspector.Core.Models;
using CommunityToolkit.Mvvm.Input;

namespace CameraInspector.App.ViewModels;

public sealed partial class MainViewModel
{
    /// <summary>
    /// Ejecuta el modo de cámara directa sobre la interfaz actualmente seleccionada.
    /// Solo utiliza mecanismos de descubrimiento; no genera un ping sweep de la subred.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanStartAlternativeScan))]
    private async Task ScanDirectCameraAsync(CancellationToken cancellationToken)
    {
        if (SelectedInterface is null)
        {
            StatusText = "Seleccioná una interfaz de red para detectar la cámara directa.";
            return;
        }

        await PrepareAlternativeScanAsync();

        try
        {
            StatusText = $"Detectando cámara directa por {SelectedInterface.Name} · sin barrido de subred...";

            await foreach (var progress in _scanner.ScanAsync(
                               SelectedInterface,
                               cancellationToken: cancellationToken,
                               mode: DiscoveryScanMode.DirectCamera))
            {
                await ProcessScanProgressAsync(progress, cancellationToken);
                StatusText = $"Cámara directa · dispositivos encontrados: {Devices.Count}";
            }

            StatusText = Devices.Count == 0
                ? "Cámara directa: no se detectaron cámaras en la interfaz seleccionada."
                : $"Cámara directa completa: {Devices.Count} cámara(s)/dispositivo(s) de imagen encontrado(s).";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Detección de cámara directa cancelada.";
        }
        finally
        {
            IsScanning = false;
            NotifyAlternativeScanCommands();
        }
    }

    /// <summary>
    /// Ejecuta el escaneo clásico de la subred de la interfaz seleccionada.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanStartAlternativeScan))]
    private async Task ScanNetworkSubnetAsync(CancellationToken cancellationToken)
    {
        if (SelectedInterface is null)
        {
            StatusText = "Seleccioná una interfaz de red para escanear su subred.";
            return;
        }

        await PrepareAlternativeScanAsync();

        try
        {
            StatusText = $"Escaneando subred de {SelectedInterface}...";

            await foreach (var progress in _scanner.ScanAsync(
                               SelectedInterface,
                               cancellationToken: cancellationToken,
                               mode: DiscoveryScanMode.NetworkSubnet))
            {
                await ProcessScanProgressAsync(progress, cancellationToken);
                StatusText = $"Subred · {progress.Scanned}/{Math.Max(progress.Total, 1)} candidatos · Cámaras visibles: {Devices.Count}";
            }

            StatusText = $"Escaneo de subred completo: {Devices.Count} cámara(s)/dispositivo(s) de imagen encontrado(s).";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Escaneo de subred cancelado.";
        }
        finally
        {
            IsScanning = false;
            NotifyAlternativeScanCommands();
        }
    }

    /// <summary>
    /// Recorre todas las interfaces activas elegibles y consolida los dispositivos encontrados.
    /// Cada interfaz se procesa por separado para evitar mezclar sockets, subredes y métricas de progreso.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanStartAlternativeScan))]
    private async Task ScanFullNetworkAsync(CancellationToken cancellationToken)
    {
        if (AvailableInterfaces.Count == 0)
        {
            StatusText = "No hay interfaces de red activas disponibles para el escaneo total.";
            return;
        }

        await PrepareAlternativeScanAsync();

        try
        {
            var processedInterfaces = 0;

            foreach (var networkInterface in AvailableInterfaces)
            {
                cancellationToken.ThrowIfCancellationRequested();
                processedInterfaces++;

                StatusText = $"Escaneo total · interfaz {processedInterfaces}/{AvailableInterfaces.Count}: {networkInterface}";

                await foreach (var progress in _scanner.ScanAsync(
                                   networkInterface,
                                   cancellationToken: cancellationToken,
                                   mode: DiscoveryScanMode.NetworkSubnet))
                {
                    await ProcessScanProgressAsync(progress, cancellationToken);
                    StatusText = $"Escaneo total · interfaz {processedInterfaces}/{AvailableInterfaces.Count} · Cámaras visibles: {Devices.Count}";
                }
            }

            StatusText = $"Escaneo total completo: {Devices.Count} cámara(s)/dispositivo(s) de imagen encontrado(s).";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Escaneo total cancelado.";
        }
        finally
        {
            IsScanning = false;
            NotifyAlternativeScanCommands();
        }
    }

    /// <summary>
    /// Limpia el estado de descubrimiento antes de iniciar uno de los modos nuevos.
    /// </summary>
    private async Task PrepareAlternativeScanAsync()
    {
        IsScanning = true;
        Devices.Clear();
        _allDiscoveredDevices.Clear();
        DiagnosticResults.Clear();
        DiagnosticHistory.Clear();
        ResolvedMainStream = null;
        ResolvedSubStream = null;
        _videoPlayerService.Stop();
        NotifyAlternativeScanCommands();

        // Yield permite que WPF pinte el estado "escaneando" antes de empezar el trabajo de red.
        await Task.Yield();
    }

    /// <summary>
    /// Procesa un resultado de descubrimiento utilizando exactamente la misma resolución de fabricante/ONVIF
    /// que el escaneo existente. Así no creamos una segunda lógica para clasificar cámaras.
    /// </summary>
    private async Task ProcessScanProgressAsync(
        ScanProgress progress,
        CancellationToken cancellationToken)
    {
        if (progress.NewlyFound is null)
            return;

        // device conserva la evidencia de red obtenida por el pipeline de discovery.
        var device = progress.NewlyFound;

        // Evitamos duplicar la misma IP cuando el escaneo total consulta varias interfaces.
        if (_allDiscoveredDevices.Any(existing =>
                string.Equals(existing.IpAddress, device.IpAddress, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        // viewModel expone el nuevo dispositivo a la interfaz WPF.
        var viewModel = new DeviceViewModel(device);
        _allDiscoveredDevices.Add(viewModel);

        // La clasificación final conserva la regla existente: solo cámaras/candidatos de imagen llegan a la lista visible.
        await ResolveDeviceAsync(device, viewModel, cancellationToken);
    }

    /// <summary>
    /// Las tres acciones comparten el mismo criterio de disponibilidad.
    /// </summary>
    private bool CanStartAlternativeScan() => !IsScanning && !IsDiagnosing;

    /// <summary>
    /// Refresca los tres comandos cuando cambia el estado de ejecución.
    /// </summary>
    private void NotifyAlternativeScanCommands()
    {
        ScanDirectCameraCommand.NotifyCanExecuteChanged();
        ScanNetworkSubnetCommand.NotifyCanExecuteChanged();
        ScanFullNetworkCommand.NotifyCanExecuteChanged();
    }
}
