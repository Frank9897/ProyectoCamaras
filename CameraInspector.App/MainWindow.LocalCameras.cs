using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CameraInspector.Core.Interfaces;
using CameraInspector.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace CameraInspector.App;

/// <summary>
/// Funciones auxiliares de la ventana principal relacionadas con la selección
/// de dispositivos y acciones de red.
/// La navegación y los perfiles de escaneo se mantienen directamente en MainWindow.xaml.
/// </summary>
public partial class MainWindow
{
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
