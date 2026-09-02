using CameraInspector.Core.Models;

namespace CameraInspector.App.ViewModels;

public sealed partial class MainViewModel
{
    /// <summary>
    /// Intenta abrir automáticamente el video al entrar a la ventana independiente.
    /// Primero usa credenciales guardadas, luego prueba sin credenciales y finalmente
    /// solicita credenciales cuando la cámara parece requerir autenticación.
    /// </summary>
    public async Task<bool> TryStartIpVideoAutomaticallyAsync(CancellationToken cancellationToken = default)
    {
        if (SelectedDevice is null)
            return false;

        var device = SelectedDevice.Device;
        StatusText = $"Probando video de {device.IpAddress}...";

        // Primero probamos credenciales ya guardadas para no pedirlas innecesariamente.
        var savedCredentials = await LoadSavedCredentialSessionAsync(cancellationToken);
        if (savedCredentials is not null && await TryResolveAndPlayMainStreamAsync(savedCredentials, cancellationToken))
            return true;

        // Una cámara recién restaurada o configurada puede publicar RTSP sin autenticación.
        var anonymous = new CredentialSession(string.Empty, string.Empty, null);
        if (await TryResolveAndPlayMainStreamAsync(anonymous, cancellationToken))
        {
            StatusText = $"Video iniciado automáticamente en {device.IpAddress} sin credenciales.";
            return true;
        }

        // Si ninguno de los dos caminos funcionó, pedimos las credenciales dentro del flujo de VIDEO.
        StatusText = "La cámara responde, pero el video requiere autenticación o una configuración de stream compatible.";
        var prompted = await PromptAndStoreCredentialsAsync();
        if (!prompted || SelectedDevice is null)
            return false;

        var enteredCredentials = await LoadSavedCredentialSessionAsync(cancellationToken);
        if (enteredCredentials is not null && await TryResolveAndPlayMainStreamAsync(enteredCredentials, cancellationToken))
            return true;

        StatusText = "Las credenciales no permitieron iniciar el video. Puede modificarlas y volver a intentar desde CREDENCIALES.";
        return false;
    }

    private async Task<CredentialSession?> LoadSavedCredentialSessionAsync(CancellationToken cancellationToken)
    {
        if (SelectedDevice?.CameraId is not int cameraId)
            return null;

        cancellationToken.ThrowIfCancellationRequested();

        var savedInfo = await _cameraCredentialStore.GetAsync(cameraId);
        if (savedInfo is null)
            return null;

        var credential = await _credentialStore.GetAsync(savedInfo.CredentialRef);
        if (credential is null)
            return null;

        return new CredentialSession(credential.Username, credential.Password, savedInfo.CredentialRef);
    }

    private async Task<bool> TryResolveAndPlayMainStreamAsync(
        CredentialSession credentials,
        CancellationToken cancellationToken)
    {
        if (SelectedDevice is null)
            return false;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var stream = await _streamUriResolver.GetMainStreamUriAsync(
                SelectedDevice.Device,
                credentials.Username,
                credentials.Password,
                cancellationToken);

            if (stream is null)
                return false;

            ResolvedMainStream = stream;
            _videoPlayerService.Play(stream, credentials.Username, credentials.Password);

            // LibVLC puede tardar en informar Playing o EncounteredError. Esperamos ambos
            // eventos para decidir si la prueba automática fue realmente aceptada.
            var player = _videoPlayerService.Player;
            var result = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            EventHandler? playingHandler = null;
            EventHandler? errorHandler = null;
            playingHandler = (_, _) => result.TrySetResult(true);
            errorHandler = (_, _) => result.TrySetResult(false);

            player.Playing += playingHandler;
            player.EncounteredError += errorHandler;
            try
            {
                if (player.IsPlaying)
                    return true;

                var completed = await Task.WhenAny(result.Task, Task.Delay(TimeSpan.FromSeconds(5), cancellationToken));
                if (completed == result.Task)
                {
                    var success = await result.Task;
                    if (success)
                        await MarkCredentialVerifiedAsync(credentials);
                    return success;
                }

                return player.IsPlaying;
            }
            finally
            {
                player.Playing -= playingHandler;
                player.EncounteredError -= errorHandler;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }
}
