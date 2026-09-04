using System.Windows;
using System.Windows.Controls;

namespace CameraInspector.App;

/// <summary>
/// Ajustes visuales del layout principal para que las áreas de trabajo aprovechen
/// el tamaño disponible sin depender de alturas fijas.
/// </summary>
public partial class MainWindow
{
    private bool _responsiveLayoutAttached;

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);

        if (_responsiveLayoutAttached)
            return;

        _responsiveLayoutAttached = true;
        MinWidth = 900;
        MinHeight = 580;
        SizeChanged += MainWindow_SizeChanged;
        ApplyResponsiveLayout();
    }

    private void MainWindow_SizeChanged(object? sender, SizeChangedEventArgs e)
    {
        ApplyResponsiveLayout();
    }

    private void ApplyResponsiveLayout()
    {
        if (Content is not Grid root || root.RowDefinitions.Count < 4)
            return;

        // La tabla y el detalle comparten el espacio disponible. Ambos poseen
        // desplazamiento propio para no perder contenido en ventanas pequeñas.
        root.RowDefinitions[2].Height = new GridLength(0.38, GridUnitType.Star);
        root.RowDefinitions[2].MinHeight = 145;
        root.RowDefinitions[3].Height = new GridLength(0.62, GridUnitType.Star);
        root.RowDefinitions[3].MinHeight = 245;
    }
}
