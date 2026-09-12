using CameraInspector.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
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

    // ETAPA 1 (plan de diagnóstico): resumen persistente "X/Y pruebas exitosas" con
    // desglose por severidad, en vez del texto fijo que había antes en el footer del
    // panel de diagnóstico. StatusText sigue existiendo para la barra de estado general
    // (transitoria); esta propiedad es específica del panel y no se pisa con otras acciones.
    [ObservableProperty]
    private string _diagnosticsSummaryText =
        "Diagnóstico sin credenciales: red, puertos, RTSP, ONVIF, Media y salud. Ejecute la batería para ver el resumen.";

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
        DiagnosticsSummaryText = "Ejecutando batería de pruebas...";

        var device = SelectedDevice.Device;
        var cameraId = SelectedDevice.CameraId;
        StatusText = $"Diagnóstico: verificando comunicación y servicios de {device.IpAddress}...";

        try
        {
            await RecheckSelectedHealthAsync();

            var results = await _diagnosticService.RunAsync(
                device,
                username: null,
                password: null,
                _diagnosticCancellation.Token);

            foreach (var result in results)
                DiagnosticResults.Add(result);

            if (cameraId is int persistedCameraId)
            {
                await _diagnosticHistoryStore.SaveAsync(persistedCameraId, results);
                await RefreshHistorySilentlyAsync(persistedCameraId);
            }

            var supported = results.Count(x => !x.NotSupported);
            var successful = results.Count(x => x.Success);
            var failures = results.Count(x => !x.Success && !x.NotSupported);
            var criticalCount = results.Count(x => x.Severity == DiagnosticSeverity.Critico);
            var warningCount = results.Count(x => x.Severity == DiagnosticSeverity.Advertencia);

            DiagnosticsSummaryText = criticalCount == 0 && warningCount == 0
                ? $"{successful}/{supported} pruebas correctas. Sin hallazgos que revisar."
                : $"{successful}/{supported} pruebas correctas · {criticalCount} crítica(s) · {warningCount} advertencia(s). Revise la columna RECOMENDACIÓN.";

            StatusText = failures == 0
                ? $"Diagnóstico completo: {successful}/{supported} pruebas correctas."
                : $"ALERTA: diagnóstico completo con {failures} prueba(s) con fallo. {successful}/{supported} correctas.";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Diagnóstico cancelado por el usuario.";
            DiagnosticsSummaryText = "Diagnóstico cancelado antes de completarse.";
        }
        catch (Exception ex)
        {
            StatusText = $"ALERTA: error general de diagnóstico: {ex.Message}";
            DiagnosticsSummaryText = $"ALERTA: no se pudo completar el diagnóstico: {ex.Message}";
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
