using System.Windows;
using CameraInspector.App;

namespace CameraInspector.App.ViewModels;

public sealed partial class MainViewModel
{
    /// <summary>
    /// Permite ingresar nuevas credenciales incluso antes de que el dispositivo
    /// haya terminado el enriquecimiento y todavía no tenga CameraId.
    /// </summary>
    public async Task<bool> PromptAndStoreCredentialsAsync()
    {
        if (SelectedDevice is null)
        {
            StatusText = "Seleccione una cámara antes de ingresar credenciales.";
            return false;
        }

        try
        {
            var cameraId = SelectedDevice.CameraId;
            if (cameraId is null)
            {
                cameraId = await _inventoryStore.UpsertAsync(SelectedDevice.Device, CancellationToken.None);
                SelectedDevice.SetCameraId(cameraId.Value);
            }

            var dialog = new CredentialsDialog(SavedCredentialUsername)
            {
                Owner = Application.Current?.Windows.OfType<Window>().FirstOrDefault(window => window.IsActive)
                         ?? Application.Current?.MainWindow
            };

            if (dialog.ShowDialog() != true)
                return false;

            if (string.IsNullOrWhiteSpace(dialog.Username))
            {
                StatusText = "El usuario no puede quedar vacío.";
                return false;
            }

            return await StoreCredentialsAsync(
                dialog.Username.Trim(),
                dialog.Password ?? string.Empty,
                cameraId.Value);
        }
        catch (Exception ex)
        {
            StatusText = $"No se pudieron guardar las credenciales: {ex.Message}";
            return false;
        }
    }

    /// <summary>
    /// Persiste de forma segura una credencial para la cámara seleccionada.
    /// La contraseña se guarda únicamente en Windows Credential Manager.
    /// </summary>
    public async Task<bool> StoreCredentialsAsync(string username, string password, int? cameraId = null)
    {
        if (SelectedDevice is null)
        {
            StatusText = "Seleccione una cámara antes de guardar credenciales.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(username))
        {
            StatusText = "El usuario no puede quedar vacío.";
            return false;
        }

        var resolvedCameraId = cameraId ?? SelectedDevice.CameraId;
        if (resolvedCameraId is null)
        {
            resolvedCameraId = await _inventoryStore.UpsertAsync(SelectedDevice.Device, CancellationToken.None);
            SelectedDevice.SetCameraId(resolvedCameraId.Value);
        }

        var normalizedUsername = username.Trim();
        var newCredentialRef = await _credentialStore.SaveAsync(normalizedUsername, password ?? string.Empty);
        var previousCredential = await _cameraCredentialStore.GetAsync(resolvedCameraId.Value);

        await _cameraCredentialStore.SaveAsync(
            resolvedCameraId.Value,
            normalizedUsername,
            newCredentialRef);

        if (previousCredential is not null && previousCredential.CredentialRef != newCredentialRef)
            await _credentialStore.DeleteAsync(previousCredential.CredentialRef);

        HasSavedCredentials = true;
        SavedCredentialUsername = normalizedUsername;
        SavedCredentialLastVerifiedAt = null;
        UseSavedCredentials = true;

        SaveCredentialsCommand.NotifyCanExecuteChanged();
        DeleteCredentialsCommand.NotifyCanExecuteChanged();
        GetMainStreamUriCommand.NotifyCanExecuteChanged();
        GetSubStreamUriCommand.NotifyCanExecuteChanged();
        RunDiagnosticsCommand.NotifyCanExecuteChanged();
        RefreshHistoryCommand.NotifyCanExecuteChanged();

        StatusText = "Credenciales guardadas de forma segura.";
        return true;
    }
}
