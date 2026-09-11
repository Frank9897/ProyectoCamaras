using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CameraInspector.Network.OnvifMedia;

namespace CameraInspector.App;

/// <summary>
/// Nombre de host, reinicio y restablecimiento de fábrica.
///
/// GENERALIZADO (antes solo funcionaba para VIVOTEK legacy, con ONVIF como único
/// respaldo): ahora las tres operaciones usan el mismo detector de fabricante
/// (DetectLegacyWriter) que el resto del ViewModel, así que también funcionan para
/// DAHUA e HIKVISION detectadas sin ONVIF, a través de sus propias implementaciones de
/// ILegacyCameraNetworkConfigurationService.
/// </summary>
public sealed partial class NetworkConfigurationEditViewModel
{
    [ObservableProperty] private string _hostname = string.Empty;

    [RelayCommand]
    private async Task LoadHostnameAsync()
    {
        if (IsApplying || IsSystemActionRunning)
            return;

        SetStatus("Consultando nombre de la cámara...");
        ValidationMessage = string.Empty;

        try
        {
            var legacy = DetectLegacyWriter();
            var credentials = await GetCredentialsAsync(legacy);
            if (credentials is null)
                return;

            if (legacy is not null)
            {
                var legacyConfiguration = await legacy.Value.Writer.GetNetworkConfigurationAsync(
                    _deviceViewModel.Device,
                    credentials.Value.Username,
                    credentials.Value.Password);

                Hostname = legacyConfiguration?.Hostname ?? string.Empty;
                HasUnsavedChanges = false;
                SetStatus(string.IsNullOrWhiteSpace(Hostname)
                    ? $"ALERTA: {legacy.Value.VendorLabel} no devolvió el nombre actual."
                    : $"OK: nombre actual = {Hostname}");
                return;
            }

            var value = await _writer.GetHostnameAsync(
                _deviceViewModel.Device,
                credentials.Value.Username,
                credentials.Value.Password);

            Hostname = value ?? string.Empty;
            HasUnsavedChanges = false;
            SetStatus(string.IsNullOrWhiteSpace(Hostname)
                ? "ALERTA: la cámara no devolvió un nombre ONVIF."
                : $"OK: nombre actual = {Hostname}");
        }
        catch (Exception ex)
        {
            SetStatus($"ALERTA: no se pudo consultar el nombre de la cámara: {ex.Message}", true);
        }
    }

    [RelayCommand]
    private async Task SetHostnameAsync()
    {
        if (IsSystemActionRunning || IsApplying)
            return;

        var value = Hostname.Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            SetStatus("ALERTA: ingrese un nombre de cámara válido.", true);
            return;
        }

        if (value.Length > 63 || !System.Text.RegularExpressions.Regex.IsMatch(value, "^[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?$"))
        {
            SetStatus("ALERTA: el nombre debe tener 1-63 caracteres y usar solo letras, números y guiones.", true);
            return;
        }

        var confirm = ThemedMessageBox.Show(
            $"CAMBIAR NOMBRE\n\nActual: {(_deviceViewModel.Device.Model ?? "desconocido")}\nNuevo:  {value}\n\n¿Continuar?",
            "Camera Inspector — Cambiar nombre",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes)
        {
            SetStatus("Cambio de nombre cancelado.");
            return;
        }

        try
        {
            IsSystemActionRunning = true;
            var legacy = DetectLegacyWriter();
            var credentials = await GetCredentialsAsync(legacy);
            if (credentials is null)
                return;

            var result = legacy is not null
                ? await legacy.Value.Writer.SetHostnameAsync(_deviceViewModel.Device, credentials.Value.Username, credentials.Value.Password, value)
                : await _writer.SetHostnameAsync(_deviceViewModel.Device, credentials.Value.Username, credentials.Value.Password, value);

            if (!result.Succeeded)
            {
                SetStatus($"ALERTA: no se pudo cambiar el nombre: {result.Message}", true);
                return;
            }

            HasUnsavedChanges = false;
            SetStatus($"OK: nombre actualizado a {value}.");
        }
        catch (Exception ex)
        {
            SetStatus($"ALERTA: error al cambiar el nombre: {ex.Message}", true);
        }
        finally
        {
            IsSystemActionRunning = false;
        }
    }

    [RelayCommand]
    private async Task RebootAsync()
    {
        if (IsSystemActionRunning || IsApplying)
            return;

        var confirm = ThemedMessageBox.Show(
            "REINICIAR CÁMARA\n\nLa cámara quedará temporalmente inaccesible. Los cambios pendientes de red deben aplicarse primero.\n\n¿Desea reiniciarla?",
            "Camera Inspector — Reiniciar cámara",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes)
        {
            SetStatus("Reinicio cancelado.");
            return;
        }

        try
        {
            IsSystemActionRunning = true;
            var legacy = DetectLegacyWriter();
            var credentials = await GetCredentialsAsync(legacy);
            if (credentials is null)
                return;

            SetStatus(legacy is not null
                ? $"Solicitando reinicio mediante {legacy.Value.VendorLabel} legacy..."
                : "Solicitando reinicio mediante ONVIF...");

            var result = legacy is not null
                ? await legacy.Value.Writer.RebootAsync(_deviceViewModel.Device, credentials.Value.Username, credentials.Value.Password)
                : await _writer.RebootAsync(_deviceViewModel.Device, credentials.Value.Username, credentials.Value.Password);

            SetStatus(result.Succeeded
                ? $"OK: reinicio solicitado. Espere a que la cámara vuelva a responder. {result.Message}"
                : $"ALERTA: la cámara no confirmó el reinicio: {result.Message}", !result.Succeeded);
        }
        catch (Exception ex)
        {
            SetStatus($"ALERTA: error al reiniciar la cámara: {ex.Message}", true);
        }
        finally
        {
            IsSystemActionRunning = false;
        }
    }

    [RelayCommand]
    private async Task FactoryResetAsync()
    {
        if (IsSystemActionRunning || IsApplying)
            return;

        var first = ThemedMessageBox.Show(
            "⚠ RESTABLECIMIENTO DE FÁBRICA\n\nEsta acción puede borrar la configuración de red, usuarios y otros parámetros.\n\n¿Desea continuar?",
            "Camera Inspector — RESTABLECIMIENTO DE FÁBRICA",
            MessageBoxButton.YesNo,
            MessageBoxImage.Error);
        if (first != MessageBoxResult.Yes)
        {
            SetStatus("Restablecimiento cancelado.");
            return;
        }

        var second = ThemedMessageBox.Show(
            "ÚLTIMA CONFIRMACIÓN\n\nDespués del restablecimiento la cámara puede cambiar de IP y requerir configuración inicial.\n\n¿CONFIRMA EL RESTABLECIMIENTO?",
            "Camera Inspector — Confirmación final",
            MessageBoxButton.YesNo,
            MessageBoxImage.Stop);
        if (second != MessageBoxResult.Yes)
        {
            SetStatus("Restablecimiento cancelado.");
            return;
        }

        try
        {
            IsSystemActionRunning = true;
            var legacy = DetectLegacyWriter();
            var credentials = await GetCredentialsAsync(legacy);
            if (credentials is null)
                return;

            SetStatus(legacy is not null
                ? $"Solicitando restablecimiento mediante {legacy.Value.VendorLabel} legacy..."
                : "Solicitando restablecimiento mediante ONVIF...");

            var result = legacy is not null
                ? await legacy.Value.Writer.FactoryResetAsync(_deviceViewModel.Device, credentials.Value.Username, credentials.Value.Password)
                : await _writer.FactoryResetAsync(_deviceViewModel.Device, credentials.Value.Username, credentials.Value.Password);

            SetStatus(result.Succeeded
                ? $"OK: restablecimiento solicitado. La cámara puede perder la IP. {result.Message}"
                : $"ALERTA: no se pudo restablecer la cámara: {result.Message}", !result.Succeeded);
        }
        catch (Exception ex)
        {
            SetStatus($"ALERTA: error en restablecimiento: {ex.Message}", true);
        }
        finally
        {
            IsSystemActionRunning = false;
        }
    }
}
