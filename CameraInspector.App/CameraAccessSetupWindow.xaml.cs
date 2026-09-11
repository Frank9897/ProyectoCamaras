using System.Windows;
using CameraInspector.App.ViewModels;
using CameraInspector.Core.Interfaces;
using CameraInspector.Core.Models;
using CameraInspector.Network.Providers.Dahua;
using CameraInspector.Network.Providers.Hikvision;
using CameraInspector.Network.Providers.Vivotek;

namespace CameraInspector.App;

/// <summary>
/// Configura el acceso administrativo real de una cámara legacy sin ONVIF.
/// Primero intenta la cuenta de fábrica sin contraseña (o, en HIKVISION, el endpoint de
/// activación) y, si la cámara rechaza o no responde correctamente, permite probar las
/// credenciales administrativas actuales antes de declarar la operación imposible.
///
/// GENERALIZADO (antes solo servía para VIVOTEK): el fabricante se detecta igual que en
/// el resto de la app (manufacturer/model/evidencia) y se usa el escritor
/// ILegacyCameraNetworkConfigurationService correspondiente (VIVOTEK/DAHUA/HIKVISION),
/// cada uno con su propio comportamiento de "primer acceso" en SetAdminPasswordAsync.
/// </summary>
public partial class CameraAccessSetupWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly DiscoveredDevice _device;
    private readonly ILegacyCameraNetworkConfigurationService _service;
    private readonly string _vendorLabel;

    public string CameraIpAddress => _device.IpAddress;
    public string CameraManufacturer => _device.Manufacturer ?? _vendorLabel;
    public string CameraModel => _device.Model ?? "—";
    public string DefaultAdminUsername => _service.DefaultAdminUsername;

    // Textos de ayuda que antes estaban fijos en el XAML asumiendo siempre VIVOTEK/root.
    public string IntroHelperText => _vendorLabel == "HIKVISION"
        ? "Esto permite activar por primera vez cámaras HIKVISION sin configurar, o cambiar la contraseña de una ya activa."
        : $"Esto permite configurar cámaras {_vendorLabel} que todavía no tienen contraseña.";

    public string ModeHelperText => _vendorLabel == "HIKVISION"
        ? $"no se solicita la contraseña actual. Camera Inspector intentará primero ACTIVAR la cámara (primer uso). Si ya está activa, se solicitarán las credenciales actuales del usuario \"{DefaultAdminUsername}\" para poder cambiarla."
        : $"no se solicita la contraseña actual. Camera Inspector probará primero con \"{DefaultAdminUsername}\" sin contraseña. Si la cámara rechaza el acceso, se solicitarán las credenciales actuales para poder editar el perfil existente.";

    public string NewPasswordLabel => $"NUEVA CONTRASEÑA {DefaultAdminUsername.ToUpperInvariant()}";

    public CameraAccessSetupWindow(MainViewModel viewModel, DiscoveredDevice device)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(device);

        InitializeComponent();

        _viewModel = viewModel;
        _device = device;
        (_service, _vendorLabel) = DetectVendorWriter(device);
        DataContext = this;

        Loaded += (_, _) =>
        {
            StatusTextBlock.Text = _vendorLabel == "HIKVISION"
                ? "Listo. Primero se intentará activar la cámara (primer uso). Si ya está activa, se pedirán las credenciales actuales."
                : $"Listo. Primero se probará \"{DefaultAdminUsername}\" sin contraseña, que es el acceso esperado para una cámara sin credenciales configuradas.";
            NewPasswordBox.Focus();
        };
    }

    /// <summary>
    /// Misma detección de fabricante que NetworkConfigurationEditViewModel.DetectLegacyWriter,
    /// duplicada acá porque esta ventana se abre independientemente del ViewModel de red.
    /// Si no se reconoce ningún fabricante legacy, se asume VIVOTEK como mejor esfuerzo
    /// (comportamiento histórico de esta ventana antes de generalizarse).
    /// </summary>
    private static (ILegacyCameraNetworkConfigurationService Service, string VendorLabel) DetectVendorWriter(DiscoveredDevice device)
    {
        var manufacturer = device.Manufacturer ?? string.Empty;
        var model = device.Model ?? string.Empty;

        if (manufacturer.Contains("Dahua", StringComparison.OrdinalIgnoreCase)
            || manufacturer.Contains("Amcrest", StringComparison.OrdinalIgnoreCase))
            return (new DahuaLegacyConfigurationService(), "DAHUA");

        if (manufacturer.Contains("Hikvision", StringComparison.OrdinalIgnoreCase)
            || model.StartsWith("DS-", StringComparison.OrdinalIgnoreCase))
            return (new HikvisionIsapiNetworkConfigurationService(), "HIKVISION");

        return (new VivotekLegacyConfigurationService(), "VIVOTEK");
    }

    private async void ApplyButton_Click(object sender, RoutedEventArgs e)
    {
        var newPassword = NewPasswordBox.Password ?? string.Empty;
        var confirmPassword = ConfirmPasswordBox.Password ?? string.Empty;

        var minLength = _vendorLabel == "HIKVISION" ? 8 : 4;
        if (newPassword.Length < minLength)
        {
            SetStatus($"ALERTA: la nueva contraseña debe tener al menos {minLength} caracteres.", true);
            NewPasswordBox.Focus();
            return;
        }

        if (!string.Equals(newPassword, confirmPassword, StringComparison.Ordinal))
        {
            SetStatus("ALERTA: las nuevas contraseñas no coinciden.", true);
            ConfirmPasswordBox.Focus();
            return;
        }

        var confirm = ThemedMessageBox.Show(
            this,
            $"Se modificará la contraseña de la cuenta administrativa \"{DefaultAdminUsername}\" directamente en la cámara.\n\n" +
            "La aplicación probará primero el acceso de fábrica. Si la cámara exige autenticación o no responde correctamente al primer intento, solicitará las credenciales actuales para probar la operación nuevamente.\n\n" +
            "¿Desea continuar?",
            "Camera Inspector — Confirmar acceso",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes)
            return;

        try
        {
            ApplyButton.IsEnabled = false;
            SetStatus("Paso 1/2: intentando el acceso de fábrica...");

            var result = await _service.SetAdminPasswordAsync(
                _device,
                string.Empty,
                newPassword);

            if (!result.Succeeded && ShouldRetryWithCredentials(result.Message))
            {
                SetStatus(
                    result.Message.Contains("HTTP 401", StringComparison.OrdinalIgnoreCase)
                        ? $"La cámara exige autenticación para modificar {DefaultAdminUsername}. Solicitando credenciales actuales..."
                        : "El CGI/ISAPI no respondió correctamente al primer intento. Puede requerir autenticación administrativa. Solicitando credenciales actuales...");

                var credentials = await _viewModel.RequestCredentialsForOperationAsync();
                if (credentials is null)
                {
                    SetStatus("Configuración cancelada: no se proporcionaron las credenciales actuales.", true);
                    return;
                }

                SetStatus("Reintentando la modificación de contraseña con las credenciales administrativas proporcionadas...");
                result = await _service.SetAdminPasswordAsync(
                    _device,
                    credentials.Value.Password,
                    newPassword);
            }

            if (!result.Succeeded)
            {
                SetStatus($"ALERTA: no se pudo configurar la contraseña. {result.Message}", true);
                return;
            }

            SetStatus("Paso 2/2: contraseña aceptada por la cámara. Sincronizando credencial local...");

            if (!await _viewModel.StoreCredentialsAsync(DefaultAdminUsername, newPassword, _viewModel.SelectedDevice?.CameraId))
            {
                SetStatus("ATENCIÓN: la cámara cambió correctamente su contraseña, pero no se pudo sincronizar la credencial local. Vuelva a revisar ACCESO.", true);
                return;
            }

            SetStatus($"OK: acceso configurado. Usuario: {DefaultAdminUsername}. La contraseña quedó sincronizada con Camera Inspector.");

            await _viewModel.TryStartIpVideoAutomaticallyAsync();

            ThemedMessageBox.Show(
                this,
                $"Acceso configurado correctamente en la cámara y sincronizado con Camera Inspector.\n\nUsuario: {DefaultAdminUsername}",
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
