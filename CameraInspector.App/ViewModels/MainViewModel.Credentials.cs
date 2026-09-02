using System.Windows;

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

            var newCredentialRef = await _credentialStore.SaveAsync(dialog.Username.Trim(), dialog.Password ?? string.Empty);
            var previousCredential = await _cameraCredentialStore.GetAsync(cameraId.Value);

            await _cameraCredentialStore.SaveAsync(
                cameraId.Value,
                dialog.Username.Trim(),
                newCredentialRef);

            if (previousCredential is not null && previousCredential.CredentialRef != newCredentialRef)
                await _credentialStore.DeleteAsync(previousCredential.CredentialRef);

            HasSavedCredentials = true;
            SavedCredentialUsername = dialog.Username.Trim();
            SavedCredentialLastVerifiedAt = null;
            UseSavedCredentials = true;
            StatusText = "Credenciales guardadas. Vuelva a probar MAIN STREAM o SUB STREAM.";
            return true;
        }
        catch (Exception ex)
        {
            StatusText = $"No se pudieron guardar las credenciales: {ex.Message}";
            return false;
        }
    }
}
