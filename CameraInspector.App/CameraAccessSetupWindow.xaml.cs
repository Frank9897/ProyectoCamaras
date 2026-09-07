using System.Windows;
using CameraInspector.App.ViewModels;
using CameraInspector.Core.Models;
using CameraInspector.Network.Providers.Vivotek;

namespace CameraInspector.App;

/// <summary>
/// Configura el acceso administrativo real de una cámara VIVOTEK legacy.
/// Primero modifica la cuenta root en la cámara y después sincroniza la credencial
/// con el almacén seguro utilizado por Camera Inspector.
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
            StatusTextBlock.Text = "Listo. En cámaras VIVOTEK nuevas o recién reiniciadas, la contraseña root puede estar vacía.";
            CurrentPasswordBox.Focus();
        };
    }

    private async void ApplyButton_Click(object sender, RoutedEventArgs e)
    {
        var currentPassword = CurrentPasswordBox.Password ?? string.Empty;
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
            "La conexión puede requerir autenticación inmediatamente después del cambio.\n\n" +
            "¿Desea continuar?",
            "Camera Inspector — Confirmar acceso",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes)
            return;

        try
        {
            ApplyButton.IsEnabled = false;
            SetStatus("Configurando acceso administrativo en la cámara... no cierre esta ventana.");

            var result = await _service.SetRootPasswordAsync(
                _device,
                currentPassword,
                newPassword);

            if (!result.Succeeded)
            {
                SetStatus($"ALERTA: no se pudo configurar la contraseña root. {result.Message}", true);
                return;
            }

            SetStatus("Contraseña root aceptada por la cámara. Guardando la misma credencial en Windows Credential Manager...");

            if (!await _viewModel.StoreCredentialsAsync("root", newPassword, _viewModel.SelectedDevice?.CameraId))
            {
                SetStatus("ATENCIÓN: la cámara cambió correctamente su contraseña, pero no se pudo sincronizar la credencial local. Vuelva a guardar CREDENCIALES.", true);
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
