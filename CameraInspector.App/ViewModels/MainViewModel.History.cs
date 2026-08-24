using CameraInspector.Core.Models;

namespace CameraInspector.App.ViewModels;

public sealed partial class MainViewModel
{
    /// <summary>
    /// Carga el historial de una cámara concreta sin pasar por el comando público de la UI.
    /// Se utiliza internamente después de guardar una nueva ejecución de diagnóstico.
    /// </summary>
    private async Task RefreshHistoryAsync(int cameraId)
    {
        // DiagnosticHistory se limpia antes de cargar la fotografía actual del historial.
        DiagnosticHistory.Clear();

        // history contiene como máximo 100 registros para evitar cargar una cantidad innecesaria de datos.
        var history = await _diagnosticHistoryStore.GetRecentAsync(cameraId, 100);

        foreach (var item in history)
            DiagnosticHistory.Add(item);
    }
}
