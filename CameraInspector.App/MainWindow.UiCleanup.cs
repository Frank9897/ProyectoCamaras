using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace CameraInspector.App;

/// <summary>
/// Limpieza visual de controles de navegación redundantes.
/// </summary>
public partial class MainWindow
{
    private void RemoveRedundantNetworkSelectors()
    {
        // Imagen 2: quitar únicamente el selector INTERFAZ y su botón ESCANEAR RED
        // del encabezado dinámico de CÁMARA IP / RED. No ocultamos el título.
        foreach (var comboBox in EnumerateUiCleanupVisualChildren(this).OfType<ComboBox>())
        {
            var binding = BindingOperations.GetBinding(comboBox, ItemsControl.ItemsSourceProperty);
            if (binding?.Path?.Path?.Equals("AvailableInterfaces", StringComparison.OrdinalIgnoreCase) == true)
                comboBox.Visibility = Visibility.Collapsed;
        }

        foreach (var button in EnumerateUiCleanupVisualChildren(this).OfType<Button>())
        {
            var commandBinding = BindingOperations.GetBinding(button, Button.CommandProperty);
            if (commandBinding?.Path?.Path?.Equals("ScanCommand", StringComparison.OrdinalIgnoreCase) == true)
                button.Visibility = Visibility.Collapsed;
        }

        // También ocultamos las etiquetas contiguas "INTERFAZ" solamente cuando pertenecen
        // al mismo bloque visual que el selector de interfaces, sin tocar otros textos.
        foreach (var label in EnumerateUiCleanupVisualChildren(this).OfType<TextBlock>())
        {
            if (!string.Equals(label.Text?.Trim(), "INTERFAZ", StringComparison.OrdinalIgnoreCase))
                continue;

            var parent = VisualTreeHelper.GetParent(label);
            if (parent is not Panel panel)
                continue;

            if (panel.Children.OfType<ComboBox>().Any(combo => combo.Visibility == Visibility.Collapsed))
                label.Visibility = Visibility.Collapsed;
        }
    }

    private static IEnumerable<DependencyObject> EnumerateUiCleanupVisualChildren(DependencyObject root)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < count; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            yield return child;
            foreach (var nested in EnumerateUiCleanupVisualChildren(child))
                yield return nested;
        }
    }
}
