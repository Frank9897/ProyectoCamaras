using System.Windows;
using System.Windows.Controls;
using CameraInspector.App.ViewModels;
using CameraInspector.Video;
using Microsoft.Extensions.DependencyInjection;

namespace CameraInspector.App;

/// <summary>
/// Controla la apertura de la vista de video IP como ventana independiente.
/// El apartado VIDEO permanece en la navegación de detalle para conservar la organización de la interfaz.
/// </summary>
public partial class MainWindow
{
    private IpCameraVideoWindow? _ipCameraVideoWindow;
    private bool _videoTabOpening;

    private void DeviceDetailTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, sender))
            return;

        if (sender is not TabControl tabs || tabs.SelectedItem is not TabItem selectedTab)
            return;

        if (!string.Equals(selectedTab.Header?.ToString(), "VIDEO", StringComparison.OrdinalIgnoreCase))
        {
            _videoTabOpening = false;
            return;
        }

        if (_videoTabOpening)
            return;

        _videoTabOpening = true;
        OpenIpCameraVideoWindow();

        // VIDEO actúa como acceso al módulo independiente y no consume espacio del detalle principal.
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (tabs.SelectedItem == selectedTab)
                tabs.SelectedIndex = 0;
        }));
    }

    private void OpenIpVideoFromPanel_Click(object sender, RoutedEventArgs e)
    {
        OpenIpCameraVideoWindow();
    }

    private void OpenIpCameraVideoWindow()
    {
        if (DataContext is not MainViewModel viewModel)
            return;

        if (viewModel.SelectedDevice is null)
        {
            ShowInformation(
                "Seleccione una cámara IP en la lista antes de abrir el reproductor de video.",
                "Camera Inspector — Video IP");
            _videoTabOpening = false;
            return;
        }

        if (_ipCameraVideoWindow is { IsVisible: true })
        {
            _ipCameraVideoWindow.Activate();
            return;
        }

        if (App.Services?.GetService<IVideoPlayerService>() is not IVideoPlayerService videoPlayerService)
        {
            ShowInformation(
                "El servicio de video IP no está disponible en la aplicación.",
                "Camera Inspector — Video IP");
            _videoTabOpening = false;
            return;
        }

        _ipCameraVideoWindow = new IpCameraVideoWindow(viewModel, videoPlayerService)
        {
            Owner = this,
            ShowInTaskbar = true
        };
        _ipCameraVideoWindow.Closed += (_, _) =>
        {
            _ipCameraVideoWindow = null;
            _videoTabOpening = false;
        };
        _ipCameraVideoWindow.Show();
    }
}
