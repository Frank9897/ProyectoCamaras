using System.Net;
using CameraInspector.Core.Interfaces;
using CameraInspector.Core.Models;
using CameraInspector.Network;
using CameraInspector.Network.Detection;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;

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

            StatusText = $"Escaneo de subred completo: {Devices.Count} cámara(s) encontrada(s).";
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

            StatusText = $"Escaneo total completo: {Devices.Count} cámara(s) encontrada(s).";
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
        await Task.Yield();
    }

    /// <summary>
    /// Publica inmediatamente la evidencia descubierta y ejecuta salud/enriquecimiento en segundo plano.
    /// Un fallo de salud jamás elimina una cámara de la lista.
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

        var classification = CameraDetectionClassifier.Classify(device);
        var viewModel = new DeviceViewModel(device);
        _allDiscoveredDevices.Add(viewModel);

        if (classification.IsLikelyCamera && !Devices.Contains(viewModel))
            Devices.Add(viewModel);

        if (SelectedDevice is null && Devices.Count == 1 && Devices.Contains(viewModel))
            SelectedDevice = viewModel;

        _ = EnrichDiscoveredDeviceAsync(device, viewModel, classification, cancellationToken);
        return Task.CompletedTask;
    }

    private async Task EnrichDiscoveredDeviceAsync(
        DiscoveredDevice device,
        DeviceViewModel viewModel,
        CameraClassificationResult initialClassification,
        CancellationToken cancellationToken)
    {
        try
        {
            await _manufacturerResolver.ResolveAsync(device, cancellationToken);
            viewModel.Refresh();

            var classification = CameraDetectionClassifier.Classify(device);
            if (!classification.IsLikelyCamera)
            {
                // Un equipo que parecía candidato por una señal genérica vuelve a ser ocultado.
                if (Devices.Contains(viewModel))
                    Devices.Remove(viewModel);
                return;
            }

            if (!Devices.Contains(viewModel))
                Devices.Add(viewModel);

            var cameraId = await _inventoryStore.UpsertAsync(device, cancellationToken);
            viewModel.SetCameraId(cameraId);
            viewModel.Refresh();

            // La comprobación de salud es independiente del enrichment y no bloquea el discovery.
            await CheckDeviceHealthAsync(device, viewModel, cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            // Un error del enrichment no invalida la evidencia que ya se mostró.
            try
            {
                await CheckDeviceHealthAsync(device, viewModel, cancellationToken);
            }
            catch
            {
            }
        }
    }

    private async Task CheckDeviceHealthAsync(
        DiscoveredDevice device,
        DeviceViewModel viewModel,
        CancellationToken cancellationToken)
    {
        var healthService = App.Services?.GetService<ICameraHealthService>();
        if (healthService is null)
            return;

        try
        {
            var health = await healthService.CheckAsync(device, cancellationToken);
            device.HealthState = health.State;
            device.CommunicationAvailable = health.CommunicationAvailable;
            device.VideoAvailable = health.VideoAvailable;
            device.AuthenticationRequired = health.AuthenticationRequired;
            device.CommunicationPort = health.CommunicationPort;
            device.CommunicationProtocol = health.Protocol;
            device.HealthMessage = health.Message;
            device.LastHealthCheckAt = health.CheckedAt;

            device.Status = health.State switch
            {
                CameraHealthState.Healthy => DeviceStatus.Online,
                CameraHealthState.NoResponse => DeviceStatus.Error,
                CameraHealthState.NoVideo => DeviceStatus.Warning,
                CameraHealthState.CommunicationOnly => DeviceStatus.Warning,
                CameraHealthState.AuthenticationRequired => DeviceStatus.Warning,
                CameraHealthState.Degraded => DeviceStatus.Warning,
                _ => device.Status
            };

            viewModel.RefreshHealth();
            viewModel.Refresh();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            device.HealthState = CameraHealthState.Degraded;
            device.HealthMessage = $"No fue posible completar la comprobación de salud: {ex.Message}";
            device.LastHealthCheckAt = DateTimeOffset.UtcNow;
            device.Status = DeviceStatus.Warning;
            viewModel.RefreshHealth();
            viewModel.Refresh();
        }
    }

    private bool CanStartAlternativeScan() => !IsScanning && !IsDiagnosing;

    private void NotifyAlternativeScanCommands()
    {
        ScanDirectCameraCommand.NotifyCanExecuteChanged();
        ScanNetworkSubnetCommand.NotifyCanExecuteChanged();
        ScanFullNetworkCommand.NotifyCanExecuteChanged();
    }
}
