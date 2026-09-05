using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CameraInspector.Core.Interfaces;
using CameraInspector.App.ViewModels;
using CameraInspector.Video;
using Microsoft.Extensions.DependencyInjection;

namespace CameraInspector.App;

/// <summary>
/// Funciones auxiliares de la ventana principal relacionadas con la selección
/// de dispositivos, acciones de red y acceso al módulo de cámaras locales USB/UVC.
/// La navegación y los perfiles de escaneo se mantienen directamente en MainWindow.xaml.
/// </summary>
public partial class MainWindow
{
    private Button? _usbNavigationButton;

    private static void OnDataGridPreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not DataGrid dataGrid) return;
        if (e.OriginalSource is not DependencyObject source) return;
        var row = FindVisualParent<DataGridRow>(source);
        if (row?.Item is null) return;
        dataGrid.SelectedItem = row.Item;
        row.IsSelected = true;
        dataGrid.Focus();
    }

    private void ConfigureLocalCameraNavigation()
    {
        if (_usbNavigationButton is not null)
            return;

        // El encabezado actual de MainWindow es el primer Grid de la raíz.
        // Agregamos aquí el acceso a USB para recuperar la navegación que existía
        // antes del rediseño, sin volver a duplicar los perfiles de escaneo IP.
        if (Content is not Grid root || root.Children.Count == 0 || root.Children[0] is not Grid header)
            return;

        if (header.Children.OfType<Button>().Any(button =>
                string.Equals(button.Content?.ToString(), "CÁMARA USB", StringComparison.OrdinalIgnoreCase)))
            return;

        header.ColumnDefinitions.Clear();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        if (header.Children.OfType<TextBlock>().FirstOrDefault() is { } title)
            Grid.SetColumn(title, 0);

        _usbNavigationButton = new Button
        {
            Content = "CÁMARA USB",
            MinWidth = 125,
            Height = 32,
            Margin = new Thickness(10, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Style = (Style)FindResource("SecondaryButton"),
            ToolTip = "Abrir el módulo de cámaras locales USB/UVC, webcams y capturadoras."
        };
        _usbNavigationButton.Click += OpenUsbCameraWindow_Click;
        Grid.SetColumn(_usbNavigationButton, 1);
        header.Children.Add(_usbNavigationButton);
    }

    private void OpenUsbCameraWindow_Click(object sender, RoutedEventArgs e)
    {
        var service = App.Services?.GetService<LocalCameraService>();
        if (service is null)
        {
            ShowInformation(
                "El servicio de cámaras locales USB/UVC no está disponible.",
                "Cámara USB");
            return;
        }

        var window = new LocalCamerasWindow(service)
        {
            Owner = this,
            ShowInTaskbar = true
        };
        window.Show();
    }

    private void NetworkConfigurationButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel mainViewModel || mainViewModel.SelectedDevice is null)
            return;

        if (!mainViewModel.SelectedDevice.OnvifSupported)
        {
            ShowInformation(
                "La configuración de red editable requiere que la cámara exponga ONVIF. La detección y el vídeo pueden seguir funcionando aunque esta función no esté disponible.",
                "Camera Inspector — Configuración de red");
            return;
        }

        var onvif = App.Services?.GetService<IOnvifDeviceService>();
        var credentials = App.Services?.GetService<ICredentialStore>();
        var cameraCredentials = App.Services?.GetService<ICameraCredentialStore>();
        if (onvif is null || credentials is null || cameraCredentials is null)
        {
            ShowInformation(
                "No están disponibles los servicios necesarios para configurar la cámara.",
                "Camera Inspector — Configuración de red");
            return;
        }

        var window = new NetworkConfigurationWindow(
            mainViewModel.SelectedDevice,
            onvif,
            credentials,
            cameraCredentials)
        {
            Owner = this,
            ShowInTaskbar = true
        };
        window.ShowDialog();
    }

    private static T? FindVisualParent<T>(DependencyObject? element)
        where T : DependencyObject
    {
        var current = element;
        while (current is not null)
        {
            if (current is T typed) return typed;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }
}
