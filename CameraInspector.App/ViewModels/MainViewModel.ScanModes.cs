using System.Net;
using CameraInspector.Core.Interfaces;
using CameraInspector.Core.Models;
using CommunityToolkit.Mvvm.Input;

namespace CameraInspector.App.ViewModels;

public sealed partial class MainViewModel
{
    /// <summary>
    /// IP opcional de la cámara que se quiere probar directamente.
    /// Vacía significa discovery-only, sin sweep de subred.
    /// </summary>
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private string _directCameraIp = string.Empty;

    /// <summary>
    /// Ejecuta el modo de cámara directa sobre la interfaz actualmente seleccionada.
    /// Cuando se informa una IP, todo el escaneo activo queda limitado a ese único host.
    /// Sin IP, solo se ejecutan los mecanismos de discovery sin barrido de subred.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanStartAlternativeScan))]
    private async Task ScanDirectCameraAsync(CancellationToken cancellationToken)
    {
        if (SelectedInterface is null)
        {
            StatusText = "Seleccioná una interfaz de red para detectar la cámara directa.";
            return;
        }

        IPAddress? targetAddress = null;
        if (!string.IsNullOrWhiteSpace(DirectCameraIp))
        {
            if (!IPAddress.TryParse(DirectCameraIp.Trim(), out targetAddress) ||
                targetAddress.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
            {
                StatusText = "La IP de cámara directa no es válida. Ejemplo: 192.168.1.50 o 169.254.10.20.";
                return;
            }

            DirectCameraIp = targetAddress.ToString();
        }

        await PrepareAlternativeScanAsync();

        try
        {
            StatusText = targetAddress is null
                ? $"Cámara directa · discovery por {SelectedInterface.Name} · sin sweep..."
                : $"Cámara directa · objetivo {targetAddress} · probando host...";

            await foreach (var progress in _scanner.ScanAsync(
                               SelectedInterface,
                               cancellationToken: cancellationToken,
                               mode: DiscoveryScanMode.DirectCamera,
                               directAddress: targetAddress))
            {
                await ProcessScanProgressAsync(progress, cancellationToken);
                StatusText = targetAddress is null
                    ? $"Cámara directa · discovery · encontrados: {Devices.Count}"
                    : $"Cámara directa · {targetAddress} · evidencia encontrada: {Devices.Count}";
            }

            StatusText = Devices.Count == 0
                ? targetAddress is null
                    ? "Cámara directa: no se detectó ninguna cámara mediante discovery en la interfaz seleccionada."
                    : $"Cámara directa: {targetAddress} no respondió como cámara ni expuso evidencia reconocible."
                : targetAddress is null
                    ? $"Discovery directo completo: {Devices.Count} cámara(s)/dispositivo(s) de imagen encontrado(s)."
                    : $"Cámara directa completa: {targetAddress} · {Devices.Count} resultado(s).";
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

        var device = progress.NewlyFound;

        // Evitamos duplicar la misma IP cuando el escaneo total consulta varias interfaces.
        if (_allDiscoveredDevices.Any(existing =>
                string.Equals(existing.IpAddress, device.IpAddress, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        var viewModel = new DeviceViewModel(device);
        _allDiscoveredDevices.Add(viewModel);
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
