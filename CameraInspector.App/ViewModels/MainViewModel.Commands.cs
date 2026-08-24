namespace CameraInspector.App.ViewModels;

public sealed partial class MainViewModel
{
    /// <summary>
    /// Actualiza el estado de los comandos cuando comienza o termina un escaneo.
    /// </summary>
    partial void OnIsScanningChanged(bool value)
    {
        // Los comandos dependen del estado de escaneo para evitar operaciones simultáneas sobre la misma cámara.
        GetMainStreamUriCommand.NotifyCanExecuteChanged();
        GetSubStreamUriCommand.NotifyCanExecuteChanged();
        RunDiagnosticsCommand.NotifyCanExecuteChanged();
        SaveCredentialsCommand.NotifyCanExecuteChanged();
        DeleteCredentialsCommand.NotifyCanExecuteChanged();
        RefreshHistoryCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Actualiza el estado de los comandos cuando comienza o termina un diagnóstico.
    /// </summary>
    partial void OnIsDiagnosingChanged(bool value)
    {
        // Se bloquean operaciones que podrían competir con la ejecución de pruebas mientras el diagnóstico está activo.
        GetMainStreamUriCommand.NotifyCanExecuteChanged();
        GetSubStreamUriCommand.NotifyCanExecuteChanged();
        RunDiagnosticsCommand.NotifyCanExecuteChanged();
        SaveCredentialsCommand.NotifyCanExecuteChanged();
        DeleteCredentialsCommand.NotifyCanExecuteChanged();
        RefreshHistoryCommand.NotifyCanExecuteChanged();
    }
}
