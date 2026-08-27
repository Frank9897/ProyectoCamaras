using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CameraInspector.Video;
using Microsoft.Extensions.DependencyInjection;

namespace CameraInspector.App;

/// <summary>
/// Extensión parcial de MainWindow para exponer la capa de cámaras locales y estabilizar
/// la selección del dispositivo que recibe las acciones del menú contextual.
/// </summary>
public partial class MainWindow
{
    static MainWindow()
    {
        // El menú contextual aparece después del clic derecho, pero necesitamos seleccionar primero la fila bajo el cursor.
        EventManager.RegisterClassHandler(
            typeof(DataGrid),
            FrameworkElement.PreviewMouseRightButtonDownEvent,
            new MouseButtonEventHandler(OnDataGridPreviewMouseRightButtonDown));

        // Mantenemos el handler para insertar la opción de cámaras locales cuando el contexto ya existe.
        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            FrameworkElement.PreviewMouseRightButtonUpEvent,
            new MouseButtonEventHandler(OnMainWindowPreviewMouseRightButtonUp));
    }

    private static void OnDataGridPreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not DataGrid dataGrid)
            return;

        // originalSource es el elemento exacto bajo el cursor durante el clic derecho.
        if (e.OriginalSource is not DependencyObject source)
            return;

        // row es la fila real de DataGrid que contiene el elemento pulsado.
        var row = FindVisualParent<DataGridRow>(source);
        if (row?.Item is null)
            return;

        // SelectedItem sincroniza la fila con MainViewModel.SelectedDevice antes de abrir el contexto.
        dataGrid.SelectedItem = row.Item;
        row.IsSelected = true;
        dataGrid.Focus();
    }

    private static void OnMainWindowPreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not MainWindow window)
            return;

        // Diferimos un ciclo para asegurar que el DataGrid haya procesado primero la selección de la fila.
        window.Dispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            new Action(() => AddLocalCameraMenuItem(window)));
    }

    private static void AddLocalCameraMenuItem(MainWindow window)
    {
        // Reutilizamos el helper privado existente en MainWindow.xaml.cs para no duplicar recorrido visual.
        var dataGrid = FindVisualChild<DataGrid>(window);
        if (dataGrid?.ContextMenu is not ContextMenu contextMenu)
            return;

        const string tag = "camera-inspector-local-cameras";
        if (contextMenu.Items.OfType<MenuItem>().Any(item => string.Equals(item.Tag as string, tag, StringComparison.Ordinal)))
            return;

        var localCameraItem = new MenuItem
        {
            Header = "Cámaras locales / USB",
            Tag = tag,
            IsEnabled = App.Services?.GetService<LocalCameraService>() is not null
        };

        localCameraItem.Click += (_, _) => OpenLocalCameraWindow(window);

        // Insertamos la opción al principio para separar claramente el flujo local del flujo IP.
        contextMenu.Items.Insert(0, new Separator());
        contextMenu.Items.Insert(0, localCameraItem);
    }

    private static void OpenLocalCameraWindow(MainWindow owner)
    {
        // service se resuelve desde DI para conservar una única instancia del acceso local.
        var service = App.Services?.GetService<LocalCameraService>();
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

    private static T? FindVisualParent<T>(DependencyObject? element)
        where T : DependencyObject
    {
        // current asciende por el árbol visual hasta encontrar el contenedor solicitado.
        var current = element;
        while (current is not null)
        {
            if (current is T typed)
                return typed;

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }
}
