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

        var availableHeight = ActualHeight;

        // En ventanas altas damos algo más de espacio a la tabla para facilitar la lectura
        // y mantenemos el detalle suficientemente grande para sus pestañas y desplazamiento.
        var listRatio = availableHeight >= 850 ? 0.42 : availableHeight <= 680 ? 0.34 : 0.38;
        var detailRatio = 1.0 - listRatio;

        root.RowDefinitions[2].Height = new GridLength(listRatio, GridUnitType.Star);
        root.RowDefinitions[2].MinHeight = 145;
        root.RowDefinitions[3].Height = new GridLength(detailRatio, GridUnitType.Star);
        root.RowDefinitions[3].MinHeight = 245;

        // Evita que el estado global crezca indefinidamente cuando el texto de diagnóstico
        // cambia durante un escaneo; el contenido largo se mantiene en una sola zona visible.
        if (root.RowDefinitions.Count > 1)
            root.RowDefinitions[1].MinHeight = 42;
    }
}
