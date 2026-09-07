using System.Windows;
using System.Windows.Controls;

namespace CameraInspector.App;

public partial class IpCameraVideoWindow
{
    private const double CompactVideoWidth = 900;

    private void LiveVideoGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is not Grid grid)
            return;

        var compact = grid.ActualWidth < CompactVideoWidth;
        ApplyLiveVideoLayout(grid, compact);
    }

    private static void ApplyLiveVideoLayout(Grid grid, bool compact)
    {
        if (grid.ColumnDefinitions.Count < 2 || grid.RowDefinitions.Count < 1)
            return;

        var viewport = grid.Children.OfType<FrameworkElement>()
            .FirstOrDefault(element => string.Equals(element.Name, "LiveVideoViewport", StringComparison.Ordinal));
        var controls = grid.Children.OfType<FrameworkElement>()
            .FirstOrDefault(element => string.Equals(element.Name, "LiveVideoControls", StringComparison.Ordinal));

        if (viewport is null || controls is null)
            return;

        if (compact)
        {
            while (grid.RowDefinitions.Count < 2)
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            grid.RowDefinitions[0].Height = new GridLength(1, GridUnitType.Star);
            grid.RowDefinitions[1].Height = GridLength.Auto;

            grid.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
            grid.ColumnDefinitions[1].Width = new GridLength(0);

            Grid.SetRow(viewport, 0);
            Grid.SetColumn(viewport, 0);
            Grid.SetColumnSpan(viewport, 1);
            viewport.Margin = new Thickness(0, 0, 0, 10);

            Grid.SetRow(controls, 1);
            Grid.SetColumn(controls, 0);
            Grid.SetColumnSpan(controls, 1);
            controls.Margin = new Thickness(0);
        }
        else
        {
            grid.RowDefinitions[0].Height = new GridLength(1, GridUnitType.Star);
            if (grid.RowDefinitions.Count > 1)
                grid.RowDefinitions[1].Height = new GridLength(0);

            grid.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
            grid.ColumnDefinitions[1].Width = new GridLength(390);

            Grid.SetRow(viewport, 0);
            Grid.SetColumn(viewport, 0);
            Grid.SetColumnSpan(viewport, 1);
            viewport.Margin = new Thickness(0, 0, 10, 0);

            Grid.SetRow(controls, 0);
            Grid.SetColumn(controls, 1);
            Grid.SetColumnSpan(controls, 1);
            controls.Margin = new Thickness(0);
        }
    }
}
