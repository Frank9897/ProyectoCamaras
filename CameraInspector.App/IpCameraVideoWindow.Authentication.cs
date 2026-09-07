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

        RefreshAccessButtons();

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
                    RefreshAccessButtons();
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
            Dispatcher.BeginInvoke(new Action(RefreshAccessButtons));
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
            RefreshAccessButtons();
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

            RefreshAccessButtons();
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
            RefreshAccessButtons();
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

    private void RefreshAccessButtons()
    {
        RefreshCredentialsButton();
        RefreshCameraAccessButton();

        var device = _viewModel.SelectedDevice?.Device;
        var supported = device is not null && IsLegacyVivotek(device);
        var hasProfile = _viewModel.HasSavedCredentials;

        // Sin perfil local guardado, una VIVOTEK legacy muestra únicamente CONFIGURAR ACCESO.
        // Una vez creado, ACCESO administra las credenciales guardadas y EDITAR PERFIL DE ACCESO
        // modifica el acceso administrativo de la propia cámara.
        if (_credentialsButton is not null && supported && !hasProfile)
        {
            _credentialsButton.Visibility = Visibility.Collapsed;
            _credentialsButton.IsEnabled = false;
        }
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
