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
        var compact = availableHeight > 0 && availableHeight <= 680;

        // En ventanas altas damos algo más de espacio a la tabla. En ventanas compactas
        // reducimos la reserva mínima de ambas zonas y dejamos que sus propios controles
        // hagan scroll cuando el contenido no entra físicamente.
        var listRatio = availableHeight >= 850 ? 0.42 : compact ? 0.34 : 0.38;
        var detailRatio = 1.0 - listRatio;

        root.RowDefinitions[2].Height = new GridLength(listRatio, GridUnitType.Star);
        root.RowDefinitions[2].MinHeight = compact ? 120 : 145;
        root.RowDefinitions[3].Height = new GridLength(detailRatio, GridUnitType.Star);
        root.RowDefinitions[3].MinHeight = compact ? 210 : 245;

        // Mantiene el área de estado legible sin permitir que un mensaje largo
        // consuma el espacio reservado para la tabla y el detalle.
        if (root.RowDefinitions.Count > 1)
            root.RowDefinitions[1].MinHeight = 42;
    }
}
