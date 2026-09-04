using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace CameraInspector.App;

/// <summary>
/// Limpieza visual de controles de navegación que quedaron redundantes después de
/// centralizar las opciones de escaneo en las pestañas de modo.
/// </summary>
public partial class MainWindow
{
    private readonly bool _uiCleanupHook = RegisterUiCleanupHook();

    private bool RegisterUiCleanupHook()
    {
        Loaded += OnUiCleanupLoaded;
        return true;
    }

    private void OnUiCleanupLoaded(object sender, RoutedEventArgs e)
    {
        Dispatcher.BeginInvoke(
            new Action(RemoveRedundantNetworkSelectors),
            DispatcherPriority.ContextIdle);
    }

    private void RemoveRedundantNetworkSelectors()
    {
        // El encabezado superior del contenido original ya no debe mostrar otro selector
        // de interfaz ni otro botón global de escaneo: los modos POR RED / DIRECTA / ENLACE
        // son ahora el punto de entrada para cada operación.
        var title = FindTextBlock(this, "CAMERA INSPECTOR");
        if (title is not null && FindVisualAncestor<Grid>(title) is { } originalHeader)
            originalHeader.Visibility = Visibility.Collapsed;

        // El selector INTERFAZ del encabezado del módulo CÁMARA IP / RED es redundante.
        // La interfaz necesaria para POR RED se sigue resolviendo internamente desde el VM.
        var interfaceLabel = FindTextBlock(this, "INTERFAZ");
        if (interfaceLabel is not null && FindVisualAncestor<StackPanel>(interfaceLabel) is { } controlsPanel)
            controlsPanel.Visibility = Visibility.Collapsed;
    }

    private static TextBlock? FindTextBlock(DependencyObject root, string text)
    {
        foreach (var child in EnumerateVisualChildren(root))
        {
            if (child is TextBlock textBlock &&
                string.Equals(textBlock.Text, text, StringComparison.OrdinalIgnoreCase))
                return textBlock;
        }
        return null;
    }

    private static T? FindVisualAncestor<T>(DependencyObject child)
        where T : DependencyObject
    {
        var current = VisualTreeHelper.GetParent(child);
        while (current is not null)
        {
            if (current is T match)
                return match;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    private static IEnumerable<DependencyObject> EnumerateVisualChildren(DependencyObject root)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < count; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            yield return child;
            foreach (var nested in EnumerateVisualChildren(child))
                yield return nested;
        }
    }
}

