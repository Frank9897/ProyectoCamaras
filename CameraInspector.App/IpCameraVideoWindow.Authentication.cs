using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using CameraInspector.App.ViewModels;

namespace CameraInspector.App;

public partial class IpCameraVideoWindow
{
    private bool _authenticationUiConfigured;
    private bool _playerErrorHooked;
    private bool _playerPlayingHooked;
    private bool _autoPlaybackStarted;
    private Button? _credentialsButton;
    private Button? _cameraAccessButton;
    private MainViewModel? _authenticationViewModel;

    static IpCameraVideoWindow()
    {
        EventManager.RegisterClassHandler(typeof(IpCameraVideoWindow), FrameworkElement.LoadedEvent, new RoutedEventHandler(OnWindowLoadedForAuthentication));
        EventManager.RegisterClassHandler(typeof(IpCameraVideoWindow), FrameworkElement.UnloadedEvent, new RoutedEventHandler(OnWindowUnloadedForAuthentication));
    }

    private static void OnWindowLoadedForAuthentication(object sender, RoutedEventArgs e)
    {
        if (sender is IpCameraVideoWindow window)
            window.ConfigureAuthenticationUi();
    }

    private static void OnWindowUnloadedForAuthentication(object sender, RoutedEventArgs e)
    {
        if (sender is IpCameraVideoWindow window && !window.IsVisible)
            window.DetachAuthenticationHandlers();
    }

    private void ConfigureAuthenticationUi()
    {
        if (!_authenticationUiConfigured)
        {
            _credentialsButton = FindButtonByContent(this, "CREDENCIALES");
            if (_credentialsButton is not null)
            {
                _credentialsButton.Command = null;
                _credentialsButton.Click += CredentialsButton_Click;
            }

            if (DataContext is MainViewModel viewModel)
            {
                _authenticationViewModel = viewModel;
                _authenticationViewModel.PropertyChanged += AuthenticationViewModel_PropertyChanged;
            }

            AddCameraAccessButton();
            _authenticationUiConfigured = true;
        }

        if (!_playerErrorHooked)
        {
            _videoPlayerService.Player.EncounteredError += Player_EncounteredError;
            _playerErrorHooked = true;
        }

        if (!_playerPlayingHooked)
        {
            _videoPlayerService.Player.Playing += Player_Playing;
            _playerPlayingHooked = true;
        }

        RefreshCredentialsButton();
        RefreshCameraAccessButton();

        if (!_autoPlaybackStarted)
        {
            _autoPlaybackStarted = true;
            Dispatcher.BeginInvoke(new Action(async () =>
            {
                try
                {
                    await _viewModel.TryStartIpVideoAutomaticallyAsync();
                }
                catch (OperationCanceledException)
                {
                    _viewModel.StatusText = "Inicio automático del video cancelado.";
                }
                catch (Exception ex)
                {
                    _viewModel.StatusText = $"No se pudo iniciar automáticamente el video: {ex.Message}";
                }
                finally
                {
                    ConfirmPlayingVideoHealth();
                    RefreshCredentialsButton();
                    RefreshCameraAccessButton();
                    RefreshButtons();
                }
            }));
        }
    }

    private void AuthenticationViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.AuthenticationRequired)
            or nameof(MainViewModel.HasSavedCredentials)
            or nameof(MainViewModel.SelectedDevice))
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                RefreshCredentialsButton();
                RefreshCameraAccessButton();
            }));
        }
    }

    private async void CredentialsButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
            return;

        try
        {
            var saved = await viewModel.PromptAndStoreCredentialsAsync();
            if (saved)
            {
                viewModel.StatusText = "Nuevas credenciales guardadas. Iniciando nuevamente el video...";
                await viewModel.TryStartIpVideoAutomaticallyAsync();
            }
        }
        catch (Exception ex)
        {
            viewModel.StatusText = $"No se pudieron guardar las credenciales: {ex.Message}";
        }
        finally
        {
            ConfirmPlayingVideoHealth();
            RefreshCredentialsButton();
            RefreshCameraAccessButton();
            RefreshButtons();
        }
    }

    private async void CameraAccessButton_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedDevice is null)
            return;

        try
        {
            var dialog = new CameraAccessSetupWindow(_viewModel, _viewModel.SelectedDevice.Device)
            {
                Owner = this,
                ShowInTaskbar = false
            };
            dialog.ShowDialog();

            RefreshCredentialsButton();
            RefreshCameraAccessButton();
            await _viewModel.TryStartIpVideoAutomaticallyAsync();
            ConfirmPlayingVideoHealth();
            RefreshButtons();
        }
        catch (Exception ex)
        {
            _viewModel.StatusText = $"ALERTA: no se pudo abrir la configuración de acceso de la cámara: {ex.Message}";
        }
    }

    private void Player_Playing(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            ConfirmPlayingVideoHealth();
            RefreshButtons();
        }));
    }

    private void Player_EncounteredError(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (DataContext is not MainViewModel viewModel)
                return;

            var ip = viewModel.SelectedDevice?.IpAddress ?? "cámara seleccionada";
            viewModel.StatusText = $"ALERTA: no se pudo iniciar el video de {ip}. Puede requerir usuario/contraseña, RTSP habilitado o una ruta de stream compatible. Puede abrir ACCESO y volver a intentar.";
            RefreshCredentialsButton();
            RefreshCameraAccessButton();
            RefreshButtons();
        }));
    }

    private void AddCameraAccessButton()
    {
        if (_cameraAccessButton is not null)
            return;

        var credentialsButton = _credentialsButton ?? FindButtonByContent(this, "CREDENCIALES");
        if (credentialsButton?.Parent is not Panel panel)
            return;

        _cameraAccessButton = new Button
        {
            Content = "CONFIGURAR ACCESO",
            MinWidth = 165,
            Height = 34,
            Margin = new Thickness(0, 0, 5, 5),
            Style = (Style)FindResource("PrimaryButton"),
            ToolTip = "Configura en la propia cámara VIVOTEK el acceso administrativo de root."
        };
        _cameraAccessButton.Click += CameraAccessButton_Click;
        panel.Children.Add(_cameraAccessButton);
    }

    private void RefreshCameraAccessButton()
    {
        if (_cameraAccessButton is null)
            return;

        var device = _viewModel.SelectedDevice?.Device;
        var supported = device is not null && IsLegacyVivotek(device);
        var hasProfile = _viewModel.HasSavedCredentials;

        if (!supported)
        {
            _cameraAccessButton.Visibility = Visibility.Collapsed;
            _cameraAccessButton.IsEnabled = false;
            return;
        }

        _cameraAccessButton.Content = hasProfile
            ? "EDITAR PERFIL DE ACCESO"
            : "CONFIGURAR ACCESO";
        _cameraAccessButton.ToolTip = hasProfile
            ? "Edita la cuenta de acceso de la cámara VIVOTEK y cambia su contraseña root."
            : "Configura por primera vez el acceso administrativo de la cámara VIVOTEK.";
        _cameraAccessButton.Visibility = Visibility.Visible;
        _cameraAccessButton.IsEnabled = true;
    }

    private void RefreshCredentialsButton()
    {
        if (_credentialsButton is null)
            return;

        var hasDevice = _viewModel.SelectedDevice is not null;
        var hasProfile = _viewModel.HasSavedCredentials;
        var supported = hasDevice && IsLegacyVivotek(_viewModel.SelectedDevice!.Device);

        // Sin perfil local guardado, para VIVOTEK legacy se muestra solamente CONFIGURAR ACCESO.
        // Una vez creado el perfil, ACCESO permite editar las credenciales guardadas y se habilita
        // por separado EDITAR PERFIL DE ACCESO para modificar la cuenta de la propia cámara.
        if (supported && !hasProfile)
        {
            _credentialsButton.Visibility = Visibility.Collapsed;
            _credentialsButton.IsEnabled = false;
            return;
        }

        _credentialsButton.Visibility = hasDevice ? Visibility.Visible : Visibility.Collapsed;
        _credentialsButton.IsEnabled = hasDevice;
        _credentialsButton.Content = "ACCESO";
        _credentialsButton.ToolTip = _viewModel.AuthenticationRequired
            ? "Edita usuario y contraseña guardados para autenticar las operaciones de Camera Inspector."
            : "Edita las credenciales guardadas para acceder a la cámara.";
    }

    private static bool IsLegacyVivotek(CameraInspector.Core.Models.DiscoveredDevice device)
    {
        var manufacturer = device.Manufacturer ?? string.Empty;
        var model = device.Model ?? string.Empty;

        return manufacturer.Contains("VIVOTEK", StringComparison.OrdinalIgnoreCase)
               || model.Contains("IP7133", StringComparison.OrdinalIgnoreCase)
               || model.Contains("IP7134", StringComparison.OrdinalIgnoreCase);
    }

    private void DetachAuthenticationHandlers()
    {
        if (_playerErrorHooked)
        {
            _videoPlayerService.Player.EncounteredError -= Player_EncounteredError;
            _playerErrorHooked = false;
        }

        if (_playerPlayingHooked)
        {
            _videoPlayerService.Player.Playing -= Player_Playing;
            _playerPlayingHooked = false;
        }

        if (_authenticationViewModel is not null)
        {
            _authenticationViewModel.PropertyChanged -= AuthenticationViewModel_PropertyChanged;
            _authenticationViewModel = null;
        }
    }

    private static Button? FindButtonByContent(DependencyObject root, string expectedContent)
    {
        for (var index = 0; index < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, index);
            if (child is Button button && string.Equals(button.Content?.ToString(), expectedContent, StringComparison.OrdinalIgnoreCase))
                return button;

            var nested = FindButtonByContent(child, expectedContent);
            if (nested is not null)
                return nested;
        }

        return null;
    }
}
