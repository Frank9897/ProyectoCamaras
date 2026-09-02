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

        var savedCredentials = await LoadSavedCredentialSessionAsync(cancellationToken);
        if (savedCredentials is not null &&
            await TryResolveAndPlayMainStreamAsync(savedCredentials, cancellationToken))
        {
            StatusText = $"Video iniciado con las credenciales guardadas en {device.IpAddress}.";
            return true;
        }

        var anonymous = new CredentialSession(string.Empty, string.Empty, null);
        if (await TryResolveAndPlayMainStreamAsync(anonymous, cancellationToken))
        {
            StatusText = $"Video iniciado automáticamente en {device.IpAddress} sin credenciales.";
            return true;
        }

        StatusText =
            $"No se pudo iniciar el video automáticamente en {device.IpAddress}. " +
            "La cámara puede requerir usuario y contraseña.";

        var prompted = await PromptAndStoreCredentialsAsync();
        if (!prompted || SelectedDevice is null)
            return false;

        var enteredCredentials = await LoadSavedCredentialSessionAsync(cancellationToken);
        if (enteredCredentials is not null &&
            await TryResolveAndPlayMainStreamAsync(enteredCredentials, cancellationToken))
        {
            StatusText = $"Video iniciado correctamente en {SelectedDevice.IpAddress}.";
            return true;
        }

        StatusText =
            "Las credenciales ingresadas no permitieron iniciar el video. " +
            "Puede abrir CREDENCIALES, modificarlas y volver a probar MAIN STREAM.";
        return false;
    }

    private async Task<CredentialSession?> LoadSavedCredentialSessionAsync(CancellationToken cancellationToken)
    {
        if (SelectedDevice?.CameraId is not int cameraId)
            return null;

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var savedInfo = await _cameraCredentialStore.GetAsync(cameraId);
            if (savedInfo is null)
                return null;

            var credential = await _credentialStore.GetAsync(savedInfo.CredentialRef);
            if (credential is null)
                return null;

            return new CredentialSession(
                credential.Username,
                credential.Password,
                savedInfo.CredentialRef);
        }
        catch
        {
            return null;
        }
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

            _videoPlayerService.Stop();
            ResolvedMainStream = stream;
            _videoPlayerService.Play(stream, credentials.Username, credentials.Password);

            var player = _videoPlayerService.Player;
            var result = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            EventHandler<EventArgs>? playingHandler = null;
            EventHandler<EventArgs>? errorHandler = null;
            playingHandler = (_, _) => result.TrySetResult(true);
            errorHandler = (_, _) => result.TrySetResult(false);

            player.Playing += playingHandler;
            player.EncounteredError += errorHandler;
            try
            {
                if (player.IsPlaying)
                    return true;

                var completed = await Task.WhenAny(
                    result.Task,
                    Task.Delay(TimeSpan.FromSeconds(5), cancellationToken));

                if (completed == result.Task)
                {
                    var success = await result.Task;
                    if (success && credentials.CredentialRef is Guid)
                        await MarkCredentialVerifiedAsync(credentials);
                    return success;
                }

                var playing = player.IsPlaying;
                if (playing && credentials.CredentialRef is Guid)
                    await MarkCredentialVerifiedAsync(credentials);

                return playing;
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
            try { _videoPlayerService.Stop(); } catch { }
            return false;
        }
    }
}
