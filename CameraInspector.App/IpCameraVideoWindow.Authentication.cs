using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CameraInspector.App.ViewModels;

namespace CameraInspector.App;

public partial class IpCameraVideoWindow
{
    private bool _authenticationUiConfigured;
    private bool _playerErrorHooked;
    private bool _autoPlaybackStarted;
    private Button? _credentialsButton;

    static IpCameraVideoWindow()
    {
        EventManager.RegisterClassHandler(
            typeof(IpCameraVideoWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnWindowLoadedForAuthentication));

        EventManager.RegisterClassHandler(
            typeof(IpCameraVideoWindow),
            FrameworkElement.UnloadedEvent,
            new RoutedEventHandler(OnWindowUnloadedForAuthentication));
    }

    private static void OnWindowLoadedForAuthentication(object sender, RoutedEventArgs e)
    {
        if (sender is not IpCameraVideoWindow window)
            return;

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
                // El botón siempre está disponible mientras exista una cámara seleccionada.
                _credentialsButton.Command = null;
                _credentialsButton.IsEnabled = true;
                _credentialsButton.Click += CredentialsButton_Click;
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
            viewModel.StatusText =
                $"No se pudo iniciar el video de {ip}. Verificá usuario y contraseña, que RTSP esté habilitado y la ruta del stream. " +
                "Podés abrir CREDENCIALES, ingresar otros datos y volver a pulsar MAIN STREAM o SUB STREAM.";

            RefreshCredentialsButton();
            RefreshButtons();
        }));
    }

    private void RefreshCredentialsButton()
    {
        if (_credentialsButton is null)
            return;

        _credentialsButton.IsEnabled = DataContext is MainViewModel viewModel
                                       && viewModel.SelectedDevice is not null;
    }

    private void DetachAuthenticationHandlers()
    {
        if (_playerErrorHooked)
        {
            _videoPlayerService.Player.EncounteredError -= Player_EncounteredError;
            _playerErrorHooked = false;
        }
    }

    private static Button? FindButtonByContent(DependencyObject root, string expectedContent)
    {
        var childrenCount = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < childrenCount; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is Button button &&
                string.Equals(button.Content?.ToString(), expectedContent, StringComparison.OrdinalIgnoreCase))
            {
                return button;
            }

            var nested = FindButtonByContent(child, expectedContent);
            if (nested is not null)
                return nested;
        }

        return null;
    }
}
