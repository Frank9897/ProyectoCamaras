using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using CameraInspector.Video;

namespace CameraInspector.App;

/// <summary>
/// Extensión parcial de MainWindow para exponer la capa de cámaras locales sin mezclarla con el flujo IP.
/// </summary>
public partial class MainWindow
{
    static MainWindow()
    {
        // Registramos un handler de clase para añadir la opción USB después de que el menú contextual existente quede construido.
        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            FrameworkElement.PreviewMouseRightButtonUpEvent,
            new MouseButtonEventHandler(OnMainWindowPreviewMouseRightButtonUp));
    }

    private static void OnMainWindowPreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not MainWindow window)
            return;

        // Diferimos la operación un ciclo para asegurar que ConfigureCameraContextMenu ya haya asignado el ContextMenu al DataGrid.
        window.Dispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            new Action(() => AddLocalCameraMenuItem(window)));
    }

    private static void AddLocalCameraMenuItem(MainWindow window)
    {
        var dataGrid = FindVisualChild<DataGrid>(window);
        if (dataGrid?.ContextMenu is not ContextMenu contextMenu)
            return;

        // tag evita añadir múltiples veces la misma opción en cada clic derecho.
        const string tag = "camera-inspector-local-cameras";
        if (contextMenu.Items.OfType<MenuItem>().Any(item => string.Equals(item.Tag as string, tag, StringComparison.Ordinal)))
            return;

        var localCameraItem = new MenuItem
        {
            Header = "Cámaras locales / USB",
            Tag = tag,
            IsEnabled = App.Services?.GetService(typeof(LocalCameraService)) is not null
        };

        localCameraItem.Click += (_, _) => OpenLocalCameraWindow(window);

        // Insertamos la opción al principio para separarla visualmente del bloque de funciones de cámaras IP.
        contextMenu.Items.Insert(0, new Separator());
        contextMenu.Items.Insert(0, localCameraItem);
    }

    private static void OpenLocalCameraWindow(MainWindow owner)
    {
        // service se resuelve desde el contenedor para conservar una única instancia del enumerador local.
        var service = App.Services?.GetService(typeof(LocalCameraService)) as LocalCameraService;
        if (service is null)
        {
            MessageBox.Show(
                "El servicio de cámaras locales no está disponible.",
                "Camera Inspector — USB",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        new LocalCamerasWindow(service)
        {
            Owner = owner
        }.ShowDialog();
    }

    private static T? FindVisualChild<T>(DependencyObject parent)
        where T : DependencyObject
    {
        if (parent is T typed)
            return typed;

        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            var result = FindVisualChild<T>(child);
            if (result is not null)
                return result;
        }

        return null;
    }
}
