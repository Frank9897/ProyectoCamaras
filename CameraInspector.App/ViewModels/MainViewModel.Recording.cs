namespace CameraInspector.App.ViewModels;

public sealed partial class MainViewModel
{
    /// <summary>
    /// Inicia una grabación de la fuente principal actualmente resuelta.
    /// La lógica de credenciales reutiliza el mismo flujo seguro que la reproducción RTSP.
    /// </summary>
    public async Task<bool> StartRecordingAsync(string filePath)
    {
        // La ruta representa el archivo local elegido explícitamente por el técnico.
        if (string.IsNullOrWhiteSpace(filePath))
        {
            StatusText = "Debe indicar un archivo de destino para la grabación.";
            return false;
        }

        // ResolvedMainStream contiene la URI RTSP real obtenida para la cámara seleccionada.
        if (ResolvedMainStream is null)
        {
            StatusText = "Primero debe resolverse el stream principal de la cámara.";
            return false;
        }

        if (IsRecording)
        {
            StatusText = "Ya existe una grabación en curso.";
            return false;
        }

        var credentials = await GetCredentialsAsync();
        if (credentials is null)
        {
            StatusText = "La grabación fue cancelada porque no se obtuvieron credenciales.";
            return false;
        }

        try
        {
            // started indica si LibVLC aceptó iniciar el segundo reproductor de grabación.
            var started = _videoPlayerService.StartRecording(
                ResolvedMainStream,
                credentials.Username,
                credentials.Password,
                filePath);

            if (!started)
            {
                StatusText = "No se pudo iniciar la grabación RTSP.";
                return false;
            }

            IsRecording = true;
            StatusText = $"Grabando RTSP en: {filePath}";
            return true;
        }
        catch (Exception ex)
        {
            StatusText = $"No se pudo iniciar la grabación: {ex.Message}";
            return false;
        }
    }

    /// <summary>
    /// Detiene la grabación actual y libera el reproductor secundario.
    /// </summary>
    public void StopRecording()
    {
        // Solo detenemos si el ViewModel realmente considera que existe una grabación activa.
        if (!IsRecording)
            return;

        try
        {
            _videoPlayerService.StopRecording();
            IsRecording = false;
            StatusText = "Grabación detenida.";
        }
        catch (Exception ex)
        {
            StatusText = $"No se pudo detener la grabación correctamente: {ex.Message}";
        }
    }
}
