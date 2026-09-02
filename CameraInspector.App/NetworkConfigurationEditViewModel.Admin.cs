using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CameraInspector.Network.OnvifMedia;

namespace CameraInspector.App;

public sealed partial class NetworkConfigurationEditViewModel
{
    [ObservableProperty]
    private string _hostname = string.Empty;

    [ObservableProperty]
    private bool _isSystemActionRunning;

    [RelayCommand]
    private async Task LoadHostnameAsync()
    {
        try
        {
            var credentials = await GetCredentialsAsync();
            if (credentials is null)
                return;

            var value = await _writer.GetHostnameAsync(
                _deviceViewModel.Device,
                credentials.Value.Username,
                credentials.Value.Password);

            Hostname = value ?? string.Empty;
            StatusText = string.IsNullOrWhiteSpace(Hostname)
                ? "La cámara no devolvió un nombre ONVIF."
                : $"Nombre actual: {Hostname}";
        }
        catch (Exception ex)
        {
            StatusText = $"No se pudo consultar el nombre de la cámara: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task SetHostnameAsync()
    {
        if (IsSystemActionRunning)
            return;

        var value = Hostname.Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            StatusText = "Ingrese un nombre de cámara válido.";
            return;
        }

        var confirm = MessageBox.Show(
            $"Cambiar nombre de cámara a:\n\n{value}\n\n¿Continuar?",
            "Camera Inspector — Cambiar nombre",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes)
            return;

        try
        {
            IsSystemActionRunning = true;
            var credentials = await GetCredentialsAsync();
            if (credentials is null)
                return;

            var result = await _writer.SetHostnameAsync(
                _deviceViewModel.Device,
                credentials.Value.Username,
                credentials.Value.Password,
                value);
            StatusText = result.Succeeded ? result.Message : $"No se pudo cambiar el nombre: {result.Message}";
        }
        catch (Exception ex)
        {
            StatusText = $"Error al cambiar el nombre: {ex.Message}";
        }
        finally
        {
            IsSystemActionRunning = false;
        }
    }

    [RelayCommand]
    private async Task RebootAsync()
    {
        if (IsSystemActionRunning)
            return;

        var confirm = MessageBox.Show(
            "La cámara se reiniciará y quedará temporalmente inaccesible.\n\n¿Desea reiniciarla?",
            "Camera Inspector — Reiniciar cámara",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes)
            return;

        try
        {
            IsSystemActionRunning = true;
            var credentials = await GetCredentialsAsync();
            if (credentials is null)
                return;

            var result = await _writer.RebootAsync(
                _deviceViewModel.Device,
                credentials.Value.Username,
                credentials.Value.Password);
            StatusText = result.Succeeded
                ? "Reinicio solicitado correctamente. La cámara puede tardar unos segundos en volver a responder."
                : $"No se pudo reiniciar la cámara: {result.Message}";
        }
        catch (Exception ex)
        {
            StatusText = $"Error al reiniciar la cámara: {ex.Message}";
        }
        finally
        {
            IsSystemActionRunning = false;
        }
    }

    [RelayCommand]
    private async Task FactoryResetAsync()
    {
        if (IsSystemActionRunning)
            return;

        var first = MessageBox.Show(
            "ATENCIÓN: esta acción restablece la cámara a valores de fábrica y puede borrar la configuración de red, usuario y demás ajustes.\n\n¿Desea continuar?",
            "Camera Inspector — RESTABLECIMIENTO DE FÁBRICA",
            MessageBoxButton.YesNo,
            MessageBoxImage.Error);
        if (first != MessageBoxResult.Yes)
            return;

        var second = MessageBox.Show(
            "ÚLTIMA CONFIRMACIÓN\n\nLa cámara puede quedar en su configuración inicial y ser inaccesible hasta volver a configurarla.\n\n¿CONFIRMA EL RESTABLECIMIENTO?",
            "Camera Inspector — Confirmación final",
            MessageBoxButton.YesNo,
            MessageBoxImage.Stop);
        if (second != MessageBoxResult.Yes)
            return;

        try
        {
            IsSystemActionRunning = true;
            var credentials = await GetCredentialsAsync();
            if (credentials is null)
                return;

            var result = await _writer.FactoryResetAsync(
                _deviceViewModel.Device,
                credentials.Value.Username,
                credentials.Value.Password);
            StatusText = result.Succeeded
                ? "Restablecimiento solicitado. La cámara puede reiniciarse y perder la configuración actual."
                : $"No se pudo restablecer la cámara: {result.Message}";
        }
        catch (Exception ex)
        {
            StatusText = $"Error en restablecimiento: {ex.Message}";
        }
        finally
        {
            IsSystemActionRunning = false;
        }
    }
}
