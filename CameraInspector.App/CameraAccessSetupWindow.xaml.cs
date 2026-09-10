using System.Windows;
using CameraInspector.App.ViewModels;
using CameraInspector.Core.Models;
using CameraInspector.Network.Providers.Vivotek;

namespace CameraInspector.App;

/// <summary>
/// Configura el acceso administrativo real de una cámara VIVOTEK legacy.
/// Primero intenta modificar root sin contraseña y, si la cámara rechaza o no responde correctamente,
/// permite probar las credenciales administrativas actuales antes de declarar la operación imposible.
/// </summary>
public partial class CameraAccessSetupWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly DiscoveredDevice _device;
    private readonly VivotekLegacyConfigurationService _service;

    public string CameraIpAddress => _device.IpAddress;
    public string CameraManufacturer => _device.Manufacturer ?? "VIVOTEK";
    public string CameraModel => _device.Model ?? "—";

    public CameraAccessSetupWindow(MainViewModel viewModel, DiscoveredDevice device)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(device);

        InitializeComponent();

        _viewModel = viewModel;
        _device = device;
        _service = new VivotekLegacyConfigurationService();
        DataContext = this;

        Loaded += (_, _) =>
        {
            StatusTextBlock.Text = "Listo. Primero se probará root sin contraseña, que es el acceso esperado para una cámara sin credenciales configuradas.";
            NewPasswordBox.Focus();
        };
    }

    private async void ApplyButton_Click(object sender, RoutedEventArgs e)
    {
        var newPassword = NewPasswordBox.Password ?? string.Empty;
        var confirmPassword = ConfirmPasswordBox.Password ?? string.Empty;

        if (newPassword.Length < 4)
        {
            SetStatus("ALERTA: la nueva contraseña debe tener al menos 4 caracteres.", true);
            NewPasswordBox.Focus();
            return;
        }

        if (!string.Equals(newPassword, confirmPassword, StringComparison.Ordinal))
        {
            SetStatus("ALERTA: las nuevas contraseñas no coinciden.", true);
            ConfirmPasswordBox.Focus();
            return;
        }

        var confirm = MessageBox.Show(
            this,
            "Se modificará la contraseña de la cuenta administrativa root directamente en la cámara.\n\n" +
            "La aplicación probará primero con root sin contraseña. Si la cámara exige autenticación o no responde correctamente al primer intento, solicitará las credenciales actuales para probar la operación nuevamente.\n\n" +
            "¿Desea continuar?",
            "Camera Inspector — Confirmar acceso",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes)
            return;

        try
        {
            ApplyButton.IsEnabled = false;
            SetStatus("Paso 1/2: intentando configurar root sin contraseña...");

            var result = await _service.SetRootPasswordAsync(
                _device,
                string.Empty,
                newPassword);

            if (!result.Succeeded && ShouldRetryWithCredentials(result.Message))
            {
                SetStatus(
                    result.Message.Contains("HTTP 401", StringComparison.OrdinalIgnoreCase)
                        ? "La cámara exige autenticación para modificar root. Solicitando credenciales actuales..."
                        : "El CGI no respondió correctamente al primer intento. Puede requerir autenticación administrativa. Solicitando credenciales actuales...");

                var credentials = await _viewModel.RequestCredentialsForOperationAsync();
                if (credentials is null)
                {
                    SetStatus("Configuración cancelada: no se proporcionaron las credenciales actuales.", true);
                    return;
                }

                SetStatus("Reintentando la modificación de root con las credenciales administrativas proporcionadas...");
                result = await _service.SetRootPasswordAsync(
                    _device,
                    credentials.Value.Password,
                    newPassword);
            }

            if (!result.Succeeded)
            {
                SetStatus($"ALERTA: no se pudo configurar la contraseña root. {result.Message}", true);
                return;
            }

            SetStatus("Paso 2/2: contraseña root aceptada por la cámara. Sincronizando credencial local...");

            if (!await _viewModel.StoreCredentialsAsync("root", newPassword, _viewModel.SelectedDevice?.CameraId))
            {
                SetStatus("ATENCIÓN: la cámara cambió correctamente su contraseña, pero no se pudo sincronizar la credencial local. Vuelva a revisar ACCESO.", true);
                return;
            }

            SetStatus("OK: acceso configurado. Usuario: root. La contraseña quedó sincronizada con Camera Inspector.");

            await _viewModel.TryStartIpVideoAutomaticallyAsync();

            MessageBox.Show(
                this,
                "Acceso configurado correctamente en la cámara y sincronizado con Camera Inspector.\n\n" +
                "Usuario: root",
                "Camera Inspector — Acceso configurado",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            SetStatus($"ALERTA: error al configurar el acceso de la cámara: {ex.Message}", true);
        }
        finally
        {
            ApplyButton.IsEnabled = true;
        }
    }

    private static bool ShouldRetryWithCredentials(string message)
    {
        return message.Contains("HTTP 401", StringComparison.OrdinalIgnoreCase)
            || message.Contains("Tiempo de espera", StringComparison.OrdinalIgnoreCase)
            || message.Contains("No se pudo conectar", StringComparison.OrdinalIgnoreCase)
            || message.Contains("No se pudo comunicar", StringComparison.OrdinalIgnoreCase);
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void SetStatus(string message, bool error = false)
    {
        StatusTextBlock.Text = message;
        StatusTextBlock.Foreground = (System.Windows.Media.Brush)FindResource(error ? "ErrBrush" : "TextDimBrush");
    }
}
