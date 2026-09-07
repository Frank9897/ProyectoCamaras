using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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

    private void ConfigureIpVideoUi(object? sender, RoutedEventArgs e)
    {
        var dataGrid = FindVisualChild<DataGrid>(this);
        if (dataGrid is not null)
        {
            dataGrid.MouseDoubleClick -= CamerasDataGrid_MouseDoubleClick;
            dataGrid.MouseDoubleClick += CamerasDataGrid_MouseDoubleClick;
        }

        if (DeviceDetailTabs is not null)
        {
            DeviceDetailTabs.SelectionChanged -= DeviceDetailTabs_SelectionChanged;
            DeviceDetailTabs.SelectionChanged += DeviceDetailTabs_SelectionChanged;
        }

        ConfigureVideoTab();
    }

    private void CamerasDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not DataGrid dataGrid || dataGrid.SelectedItem is null)
            return;

        OpenIpCameraVideoWindow();
        e.Handled = true;
    }

    private void ConfigureVideoTab()
    {
        if (IpVideoTab is null)
            return;

        var grid = new Grid
        {
            Margin = new Thickness(10)
        };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var infoPanel = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 18, 0)
        };
        infoPanel.Children.Add(new TextBlock
        {
            Text = "VIDEO IP",
            FontFamily = new FontFamily("Consolas"),
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)FindResource("AccentBrush")
        });
        infoPanel.Children.Add(new TextBlock
        {
            Text = "Abrir la reproducción de la cámara seleccionada en la ventana de video.",
            Margin = new Thickness(0, 3, 0, 0),
            FontSize = 10,
            Foreground = (Brush)FindResource("TextDimBrush")
        });

        var playButton = new Button
        {
            Content = "REPRODUCIR VIDEO",
            Width = 175,
            Height = 34,
            VerticalAlignment = VerticalAlignment.Center,
            Style = (Style)FindResource("PrimaryButton")
        };
        playButton.Click += OpenIpVideoFromPanel_Click;

        Grid.SetColumn(infoPanel, 0);
        Grid.SetColumn(playButton, 1);
        grid.Children.Add(infoPanel);
        grid.Children.Add(playButton);

        IpVideoTab.Content = grid;
    }

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

        // VIDEO se mantiene como acceso al módulo independiente; el botón es la acción explícita.
        if (!selectedTab.IsMouseDirectlyOver)
            return;

        if (_videoTabOpening)
            return;

        _videoTabOpening = true;
        OpenIpCameraVideoWindow();

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
