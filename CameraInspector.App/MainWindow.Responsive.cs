using System.Windows;
using System.Windows.Controls;

namespace CameraInspector.App;

/// <summary>
/// Mantiene el layout principal estable y adaptable sin cambiar las proporciones
/// durante cada movimiento del mouse. Las zonas con contenido usan su propio scroll.
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
        ApplyResponsiveLayout();
    }

    private void ApplyResponsiveLayout()
    {
        if (Content is not Grid root || root.RowDefinitions.Count < 4)
            return;

        // Las dos áreas principales reciben una proporción estable del espacio.
        // No se recalculan en cada pixel de resize para evitar saltos visuales.
        root.RowDefinitions[2].Height = new GridLength(2, GridUnitType.Star);
        root.RowDefinitions[2].MinHeight = 120;

        root.RowDefinitions[3].Height = new GridLength(3, GridUnitType.Star);
        root.RowDefinitions[3].MinHeight = 210;

        // El estado global conserva un mínimo razonable, pero el contenido puede
        // crecer y envolverse cuando el ancho disponible es menor.
        if (root.RowDefinitions.Count > 1)
            root.RowDefinitions[1].MinHeight = 42;
    }
}
