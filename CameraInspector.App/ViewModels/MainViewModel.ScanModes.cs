using System.Net;
using CameraInspector.Core.Interfaces;
using CameraInspector.Core.Models;
using CameraInspector.Network;
using CommunityToolkit.Mvvm.Input;

namespace CameraInspector.App.ViewModels;

public sealed partial class MainViewModel
{
    /// <summary>
    /// IP opcional de la cámara que se quiere probar directamente.
    /// Vacía significa descubrimiento directo sin conocer previamente la IP.
    /// </summary>
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private string _directCameraIp = string.Empty;

    /// <summary>
    /// Ejecuta el modo de cámara directa.
    ///
    /// Sin IP: prepara el enlace APIPA del adaptador para permitir el descubrimiento
    /// de cámaras VIVOTEK sin DHCP y luego prioriza los mecanismos de discovery.
    ///
    /// Con IP: limita las pruebas activas a ese único host.
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
            string? apipaStatus = null;
            if (targetAddress is null)
            {
                StatusText = $"Cámara directa · preparando enlace APIPA en {SelectedInterface.Name}...";
                var apipa = await VivotekDirectLinkAddressService.EnsureApipaAddressAsync(
                    SelectedInterface.Name,
                    SelectedInterface.InterfaceId,
                    cancellationToken);
                apipaStatus = apipa.Message;
            }

            StatusText = targetAddress is null
                ? $"Cámara directa · {apipaStatus}"
                : $"Cámara directa · objetivo {targetAddress} · probando host...";

            await foreach (var progress in _scanner.ScanAsync(
                               SelectedInterface,
                               cancellationToken: cancellationToken,
                               mode: DiscoveryScanMode.DirectCamera,
                               directAddress: targetAddress))
            {
                await ProcessScanProgressAsync(progress, cancellationToken);

                StatusText = targetAddress is null
                    ? $"Cámara directa · discovery · cámaras detectadas: {Devices.Count}"
                    : $"Cámara directa · {targetAddress} · evidencia encontrada: {Devices.Count}";
            }

            if (targetAddress is null)
            {
                if (Devices.Count == 0)
                {
                    StatusText = $"Cámara directa: no se detectó ninguna cámara. {apipaStatus ?? string.Empty} Verificá enlace Ethernet/PoE y que la cámara esté encendida.";
                }
                else if (Devices.Count == 1)
                {
                    SelectedDevice ??= Devices[0];
                    StatusText = $"Cámara detectada: {Devices[0].Manufacturer ?? "fabricante desconocido"} · {Devices[0].IpAddress}. Revisá CREDENCIALES para autenticarla si corresponde.";
                }
                else
                {
                    StatusText = $"Discovery directo completo: {Devices.Count} cámaras/dispositivos encontrados. Seleccioná uno para ingresar credenciales o probar video.";
                }
            }
            else
            {
                StatusText = Devices.Count == 0
                    ? $"Cámara directa: {targetAddress} no respondió como cámara ni expuso evidencia reconocible."
                    : $"Cámara directa completa: {targetAddress} · {Devices.Count} resultado(s).";
            }
        }
        catch (OperationCanceledException)
        {
            StatusText = "Detección de cámara directa cancelada.";
        }
        catch (Exception ex)
        {
            StatusText = $"Error durante la detección de cámara directa: {ex.Message}";
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
    /// Publica inmediatamente la evidencia descubierta. El enriquecimiento del dispositivo
    /// continúa en segundo plano y ya no bloquea la aparición del resultado en la UI.
    /// </summary>
    private Task ProcessScanProgressAsync(
        ScanProgress progress,
        CancellationToken cancellationToken)
    {
        if (progress.NewlyFound is null)
            return Task.CompletedTask;

        var device = progress.NewlyFound;

        if (_allDiscoveredDevices.Any(existing =>
                string.Equals(existing.IpAddress, device.IpAddress, StringComparison.OrdinalIgnoreCase)))
        {
            return Task.CompletedTask;
        }

        var viewModel = new DeviceViewModel(device);
        _allDiscoveredDevices.Add(viewModel);

        // Las fuentes de discovery ya entregan evidencia suficiente para mostrar el equipo.
        var cameraCandidate = device.CameraEvidence ||
                              device.OnvifSupported ||
                              device.RtspSupported ||
                              device.DetectionEvidence.Any(item => item.IsCameraEvidence);

        if (cameraCandidate && !Devices.Contains(viewModel))
            Devices.Add(viewModel);

        if (SelectedDevice is null && Devices.Count == 1 && Devices.Contains(viewModel))
            SelectedDevice = viewModel;

        // No esperamos fabricante/ONVIF/inventario. El usuario ve el dispositivo inmediatamente.
        _ = EnrichDiscoveredDeviceAsync(device, viewModel, cancellationToken);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Enriquece una cámara descubierta sin bloquear el pipeline de discovery.
    /// Las consultas ONVIF pesadas quedan para las operaciones específicas del dispositivo.
    /// </summary>
    private async Task EnrichDiscoveredDeviceAsync(
        DiscoveredDevice device,
        DeviceViewModel viewModel,
        CancellationToken cancellationToken)
    {
        try
        {
            await _manufacturerResolver.ResolveAsync(device, cancellationToken);
            viewModel.Refresh();

            var cameraCandidate = device.CameraEvidence ||
                                  device.OnvifSupported ||
                                  device.RtspSupported ||
                                  device.DetectionEvidence.Any(item => item.IsCameraEvidence);

            if (!cameraCandidate)
                return;

            if (!Devices.Contains(viewModel))
                Devices.Add(viewModel);

            var cameraId = await _inventoryStore.UpsertAsync(device, cancellationToken);
            viewModel.SetCameraId(cameraId);
        }
        catch (OperationCanceledException)
        {
            // El cierre/cancelación del escaneo no debe producir errores visibles.
        }
        catch
        {
            // La evidencia descubierta sigue siendo válida aunque el enriquecimiento falle.
        }
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
