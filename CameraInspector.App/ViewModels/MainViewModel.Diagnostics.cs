using CameraInspector.Core.Models;
using CommunityToolkit.Mvvm.Input;

namespace CameraInspector.App.ViewModels;

/// <summary>
/// Diagnóstico de escritorio: no obliga a introducir credenciales.
/// Las pruebas de autenticación/ONVIF informan el fallo y permiten que el técnico
/// decida después si desea abrir vídeo o una operación autenticada.
/// </summary>
public sealed partial class MainViewModel
{
    private CancellationTokenSource? _diagnosticCancellation;

    [RelayCommand]
    private async Task RunQuickDiagnosticsAsync()
    {
        if (SelectedDevice is null)
        {
            StatusText = "Seleccione una cámara antes de ejecutar el diagnóstico.";
            return;
        }

        if (IsDiagnosing)
            return;

        _diagnosticCancellation?.Dispose();
        _diagnosticCancellation = new CancellationTokenSource();
        IsDiagnosing = true;
        DiagnosticResults.Clear();

        var device = SelectedDevice.Device;
        StatusText = $"Diagnóstico: verificando comunicación y servicios de {device.IpAddress}...";

        try
        {
            // Primero comprobamos salud sin solicitar usuario/contraseña.
            await RecheckSelectedHealthAsync();

            var results = await _diagnosticService.RunAsync(
                device,
                username: null,
                password: null,
                _diagnosticCancellation.Token);

            foreach (var result in results)
                DiagnosticResults.Add(result);

            if (device.CameraId is int cameraId)
            {
                await _diagnosticHistoryStore.SaveAsync(cameraId, results);
                await RefreshHistorySilentlyAsync(cameraId);
            }

            var supported = results.Count(x => !x.NotSupported);
            var successful = results.Count(x => x.Success);
            var failures = results.Count(x => !x.Success && !x.NotSupported);

            StatusText = failures == 0
                ? $"Diagnóstico completo: {successful}/{supported} pruebas correctas."
                : $"ALERTA: diagnóstico completo con {failures} prueba(s) con fallo. {successful}/{supported} correctas.";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Diagnóstico cancelado por el usuario.";
        }
        catch (Exception ex)
        {
            StatusText = $"ALERTA: error general de diagnóstico: {ex.Message}";
        }
        finally
        {
            IsDiagnosing = false;
            _diagnosticCancellation?.Dispose();
            _diagnosticCancellation = null;
        }
    }

    [RelayCommand]
    private void CancelDiagnostics()
    {
        if (!IsDiagnosing)
            return;

        _diagnosticCancellation?.Cancel();
        StatusText = "Cancelando diagnóstico...";
    }

    private async Task RefreshHistorySilentlyAsync(int cameraId)
    {
        DiagnosticHistory.Clear();
        var history = await _diagnosticHistoryStore.GetRecentAsync(cameraId, 100);
        foreach (var item in history)
            DiagnosticHistory.Add(item);
    }
}
