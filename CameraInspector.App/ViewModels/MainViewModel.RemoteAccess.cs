using System.Net;
using CameraInspector.Core.Interfaces;
using CameraInspector.Core.Models;
using CameraInspector.Network;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;

namespace CameraInspector.App.ViewModels;

/// <summary>
/// Acceso mediante un host/servidor de enlace y un puerto configurable.
/// No asume VAST, VSS ni un fabricante concreto.
/// </summary>
public sealed partial class MainViewModel
{
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(TestRemoteConnectionCommand))]
    [NotifyCanExecuteChangedFor(nameof(SearchRemoteCamerasCommand))]
    private string _remoteHost = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(TestRemoteConnectionCommand))]
    [NotifyCanExecuteChangedFor(nameof(SearchRemoteCamerasCommand))]
    private string _remotePort = "3443";

    [ObservableProperty] private string _remoteStatus = "Sin conexión configurada.";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(TestRemoteConnectionCommand))]
    [NotifyCanExecuteChangedFor(nameof(SearchRemoteCamerasCommand))]
    private bool _remoteBusy;

    [RelayCommand(CanExecute = nameof(CanRunRemoteAction))]
    private async Task TestRemoteConnectionAsync(CancellationToken cancellationToken)
    {
        if (!TryBuildRemoteTarget(out var target)) return;

        RemoteBusy = true;
        try
        {
            RemoteStatus = $"Probando enlace {target.Host}:{target.Port}...";
            var service = App.Services?.GetService<IRemoteCameraDiscoveryService>()
                ?? new RemoteEndpointDiscoveryService();
            var result = await service.ProbeAsync(target, cancellationToken);
            RemoteStatus = result.Connected
                ? $"ENLACE OK · {target.Host}:{target.Port} · protocolo: {result.Protocol}"
                : $"ALERTA · no se pudo establecer el enlace: {result.Message}";
            StatusText = RemoteStatus;
        }
        catch (OperationCanceledException)
        {
            RemoteStatus = "Prueba de enlace cancelada.";
        }
        catch (Exception ex)
        {
            RemoteStatus = $"ALERTA: {ex.Message}";
            StatusText = RemoteStatus;
        }
        finally
        {
            RemoteBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanRunRemoteAction))]
    private async Task SearchRemoteCamerasAsync(CancellationToken cancellationToken)
    {
        if (!TryBuildRemoteTarget(out var target)) return;

        RemoteBusy = true;
        try
        {
            RemoteStatus = $"Buscando cámaras en {target.Host}:{target.Port}...";
            StatusText = RemoteStatus;
            var service = App.Services?.GetService<IRemoteCameraDiscoveryService>()
                ?? new RemoteEndpointDiscoveryService();
            var devices = await service.DiscoverAsync(target, cancellationToken);

            if (devices.Count == 0)
            {
                RemoteStatus =
                    "ENLACE SIN CÁMARAS IDENTIFICADAS · el endpoint respondió pero no expuso una firma de cámara reconocible. " +
                    "Esto no se interpreta como error de red.";
                StatusText = RemoteStatus;
                return;
            }

            foreach (var device in devices)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await ProcessScanProgressAsync(new ScanProgress(1, devices.Count, device), cancellationToken);
            }

            RemoteStatus = $"BÚSQUEDA COMPLETA · {devices.Count} resultado(s) desde {target.Host}:{target.Port}.";
            StatusText = RemoteStatus;
        }
        catch (OperationCanceledException)
        {
            RemoteStatus = "Búsqueda remota cancelada.";
        }
        catch (Exception ex)
        {
            RemoteStatus = $"ALERTA: {ex.Message}";
            StatusText = RemoteStatus;
        }
        finally
        {
            RemoteBusy = false;
        }
    }

    private bool CanRunRemoteAction()
        => !RemoteBusy
           && !string.IsNullOrWhiteSpace(RemoteHost)
           && int.TryParse(RemotePort, out var port)
           && port is >= 1 and <= 65535;

    private bool TryBuildRemoteTarget(out RemoteConnectionTarget target)
    {
        target = new RemoteConnectionTarget
        {
            Host = RemoteHost.Trim(),
            Port = 0
        };

        if (string.IsNullOrWhiteSpace(RemoteHost))
        {
            RemoteStatus = "Ingresá el host o IP del equipo de enlace.";
            StatusText = RemoteStatus;
            return false;
        }

        if (!int.TryParse(RemotePort, out var port) || port is < 1 or > 65535)
        {
            RemoteStatus = "El puerto debe ser un número entre 1 y 65535. Ejemplo: 3443.";
            StatusText = RemoteStatus;
            return false;
        }

        if (Uri.CheckHostName(RemoteHost.Trim()) == UriHostNameType.Unknown &&
            !IPAddress.TryParse(RemoteHost.Trim(), out _))
        {
            RemoteStatus = "El host no tiene un formato válido.";
            StatusText = RemoteStatus;
            return false;
        }

        target = new RemoteConnectionTarget
        {
            Host = RemoteHost.Trim(),
            Port = port,
            Protocol = "AUTO"
        };
        return true;
    }
}
