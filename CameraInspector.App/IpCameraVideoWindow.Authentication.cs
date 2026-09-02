using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using CameraInspector.App.ViewModels;

namespace CameraInspector.App;

public partial class IpCameraVideoWindow
{
    private bool _authenticationUiConfigured;
    private bool _playerErrorHooked;
    private bool _autoPlaybackStarted;
    private Button? _credentialsButton;
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

            _authenticationUiConfigured = true;
        }

        if (!_playerErrorHooked)
        {
            _videoPlayerService.Player.EncounteredError += Player_EncounteredError;
            _playerErrorHooked = true;
        }

        RefreshCredentialsButton();

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
                    RefreshCredentialsButton();
                    RefreshButtons();
                }
            }));
        }
    }

    private void AuthenticationViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.AuthenticationRequired))
            Dispatcher.BeginInvoke(new Action(RefreshCredentialsButton));
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
            RefreshCredentialsButton();
            RefreshButtons();
        }
    }

    private void Player_EncounteredError(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (DataContext is not MainViewModel viewModel)
                return;

            var ip = viewModel.SelectedDevice?.IpAddress ?? "cámara seleccionada";
            viewModel.StatusText = $"ALERTA: no se pudo iniciar el video de {ip}. Puede requerir usuario/contraseña, RTSP habilitado o una ruta de stream compatible. Puede abrir CREDENCIALES y volver a intentar.";
            RefreshCredentialsButton();
            RefreshButtons();
        }));
    }

    private void DetachAuthenticationHandlers()
    {
        if (_playerErrorHooked)
        {
            _videoPlayerService.Player.EncounteredError -= Player_EncounteredError;
            _playerErrorHooked = false;
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
