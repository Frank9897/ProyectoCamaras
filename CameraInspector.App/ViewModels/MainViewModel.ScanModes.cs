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
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private string _directCameraIp = string.Empty;

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
            if (!IPAddress.TryParse(DirectCameraIp.Trim(), out targetAddress) || targetAddress.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
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
                var apipa = await VivotekDirectLinkAddressService.EnsureApipaAddressAsync(SelectedInterface.Name, SelectedInterface.InterfaceId, cancellationToken);
                apipaStatus = apipa.Message;
            }

            StatusText = targetAddress is null
                ? $"Cámara directa · {apipaStatus}"
                : $"Cámara directa · objetivo {targetAddress} · probando host...";

            await foreach (var progress in _scanner.ScanAsync(SelectedInterface, cancellationToken: cancellationToken, mode: DiscoveryScanMode.DirectCamera, directAddress: targetAddress))
            {
                await ProcessScanProgressAsync(progress, cancellationToken);
                StatusText = targetAddress is null
                    ? $"Cámara directa · discovery · cámaras detectadas: {Devices.Count}"
                    : $"Cámara directa · {targetAddress} · resultados: {Devices.Count}";
            }

            if (targetAddress is not null && Devices.Count == 0)
            {
                // En una prueba dirigida el técnico ya declaró que la IP corresponde al objetivo.
                // Conservamos el objetivo aunque no responda para poder mostrar la alerta y las acciones.
                var placeholder = new DiscoveredDevice { IpAddress = targetAddress.ToString(), Status = DeviceStatus.Error };
                placeholder.HealthState = CameraHealthState.NoResponse;
                placeholder.HealthMessage = "ALERTA: la IP objetivo no respondió durante la detección dirigida.";
                placeholder.LastHealthCheckAt = DateTimeOffset.UtcNow;
                placeholder.AddEvidence("DirectTarget", 0.1, "Objetivo ingresado manualmente; sin respuesta", false);
                var vm = new DeviceViewModel(placeholder);
                _allDiscoveredDevices.Add(vm);
                Devices.Add(vm);
                SelectedDevice = vm;
                vm.RefreshHealth();
                StatusText = $"ALERTA: {targetAddress} no responde. El objetivo permanece visible para diagnóstico y reintento.";
            }
            else if (Devices.Count == 0)
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
                StatusText = $"Discovery directo completo: {Devices.Count} cámaras/dispositivos encontrados.";
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
            await foreach (var progress in _scanner.ScanAsync(SelectedInterface, cancellationToken: cancellationToken, mode: DiscoveryScanMode.NetworkSubnet))
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
                await foreach (var progress in _scanner.ScanAsync(networkInterface, cancellationToken: cancellationToken, mode: DiscoveryScanMode.NetworkSubnet))
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
    /// Fusiona evidencias sucesivas del mismo IP. Esto es esencial porque el orchestrator
    /// publica cada protocolo cuando termina y una evidencia fuerte puede llegar después de una débil.
    /// </summary>
    private Task ProcessScanProgressAsync(ScanProgress progress, CancellationToken cancellationToken)
    {
        if (progress.NewlyFound is null)
            return Task.CompletedTask;

        var incoming = progress.NewlyFound;
        var existingViewModel = _allDiscoveredDevices.FirstOrDefault(existing => string.Equals(existing.IpAddress, incoming.IpAddress, StringComparison.OrdinalIgnoreCase));

        if (existingViewModel is not null)
        {
            MergeDevice(existingViewModel.Device, incoming);
            var classification = CameraDetectionClassifier.Classify(existingViewModel.Device);
            existingViewModel.Refresh();
            existingViewModel.RefreshHealth();

            if (classification.IsLikelyCamera && !Devices.Contains(existingViewModel))
                Devices.Add(existingViewModel);
            else if (!classification.IsLikelyCamera && Devices.Contains(existingViewModel))
                Devices.Remove(existingViewModel);

            _ = EnrichDiscoveredDeviceAsync(existingViewModel.Device, existingViewModel, classification, cancellationToken);
            return Task.CompletedTask;
        }

        var device = incoming;
        var initialClassification = CameraDetectionClassifier.Classify(device);
        var viewModel = new DeviceViewModel(device);
        _allDiscoveredDevices.Add(viewModel);

        if (initialClassification.IsLikelyCamera)
            Devices.Add(viewModel);

        if (SelectedDevice is null && Devices.Count == 1)
            SelectedDevice = viewModel;

        _ = EnrichDiscoveredDeviceAsync(device, viewModel, initialClassification, cancellationToken);
        return Task.CompletedTask;
    }

    private static void MergeDevice(DiscoveredDevice target, DiscoveredDevice incoming)
    {
        target.MacAddress ??= incoming.MacAddress;
        target.Hostname ??= incoming.Hostname;
        target.Manufacturer ??= incoming.Manufacturer;
        target.Model ??= incoming.Model;
        target.FirmwareVersion ??= incoming.FirmwareVersion;
        target.SerialNumber ??= incoming.SerialNumber;
        target.OnvifSupported |= incoming.OnvifSupported;
        target.OnvifProfile ??= incoming.OnvifProfile;
        target.OnvifDeviceServiceXAddr ??= incoming.OnvifDeviceServiceXAddr;
        target.OnvifMediaServiceXAddr ??= incoming.OnvifMediaServiceXAddr;
        target.OnvifImagingServiceXAddr ??= incoming.OnvifImagingServiceXAddr;
        target.OnvifPtzServiceXAddr ??= incoming.OnvifPtzServiceXAddr;
        target.OnvifEventsServiceXAddr ??= incoming.OnvifEventsServiceXAddr;
        target.RtspSupported |= incoming.RtspSupported;
        target.HttpSupported |= incoming.HttpSupported;
        target.HttpsSupported |= incoming.HttpsSupported;
        target.HttpPort ??= incoming.HttpPort;
        target.RtspPort ??= incoming.RtspPort;
        target.CameraEvidence |= incoming.CameraEvidence;
        target.LastSeenAt = incoming.LastSeenAt > target.LastSeenAt ? incoming.LastSeenAt : target.LastSeenAt;

        foreach (var evidence in incoming.DetectionEvidence)
            target.AddEvidence(evidence.Method, evidence.Confidence, evidence.Details, evidence.IsCameraEvidence);
    }

    private async Task EnrichDiscoveredDeviceAsync(DiscoveredDevice device, DeviceViewModel viewModel, CameraClassificationResult initialClassification, CancellationToken cancellationToken)
    {
        try
        {
            await _manufacturerResolver.ResolveAsync(device, cancellationToken);
            viewModel.Refresh();

            var classification = CameraDetectionClassifier.Classify(device);
            if (!classification.IsLikelyCamera)
            {
                if (Devices.Contains(viewModel))
                    Devices.Remove(viewModel);
                return;
            }

            if (!Devices.Contains(viewModel))
                Devices.Add(viewModel);

            var cameraId = await _inventoryStore.UpsertAsync(device, cancellationToken);
            viewModel.SetCameraId(cameraId);
            viewModel.Refresh();
            await CheckDeviceHealthAsync(device, viewModel, cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            try { await CheckDeviceHealthAsync(device, viewModel, cancellationToken); } catch { }
        }
    }

    private async Task CheckDeviceHealthAsync(DiscoveredDevice device, DeviceViewModel viewModel, CancellationToken cancellationToken)
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
            device.HealthMessage = $"ALERTA: no fue posible completar la comprobación de salud: {ex.Message}";
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
