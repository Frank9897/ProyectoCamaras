using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CameraInspector.App.Responsive;

/// <summary>
/// Ajustes de composición que complementan el comportamiento de tamaño de la ventana.
/// Mantiene la interfaz utilizable cuando el usuario reduce el ancho disponible y evita
/// que los bloques con acciones laterales queden comprimidos o se corten.
/// </summary>
internal static class ResponsiveLayoutManager
{
    private sealed class LayoutState
    {
        public readonly Dictionary<UIElement, (int Row, int Column)> Positions = new();
        public readonly List<RowDefinition> AddedRows = new();
    }

    private static readonly ConditionalWeakTable<Grid, LayoutState> States = new();

    public static void Apply(Window window)
    {
        if (!window.IsLoaded)
            return;

        var compact = window.ActualWidth < 1050;
        var veryCompact = window.ActualWidth < 860;

        if (window.Content is not DependencyObject root)
            return;

        foreach (var grid in FindVisualChildren<Grid>(root))
        {
            AdaptActionGrid(grid, compact);
            AdaptHeaderGrid(grid, veryCompact);
        }

        // Los DataGrid ya tienen scroll horizontal; aquí nos aseguramos de que no
        // intenten ocupar menos de un ancho razonable dentro de la ventana.
        foreach (var dataGrid in FindVisualChildren<DataGrid>(root))
        {
            dataGrid.MinColumnWidth = 55;
        }
    }

    private static void AdaptActionGrid(Grid grid, bool compact)
    {
        if (grid.ColumnDefinitions.Count < 2 || grid.ColumnDefinitions.Count > 3)
            return;

        var button = grid.Children
            .OfType<Button>()
            .FirstOrDefault(child => Grid.GetColumn(child) > 0);

        if (button is null)
            return;

        var state = States.GetOrCreateValue(grid);

        if (!compact)
        {
            Restore(grid, state);
            return;
        }

        if (state.Positions.Count == 0)
        {
            foreach (UIElement child in grid.Children)
                state.Positions[child] = (Grid.GetRow(child), Grid.GetColumn(child));
        }

        // En ancho reducido, las acciones pasan debajo del contenido en vez de
        // forzar una segunda columna estrecha. Esto permite que futuras acciones
        // agregadas a estos bloques sigan el mismo patrón.
        if (grid.RowDefinitions.Count < 2)
        {
            var row = new RowDefinition { Height = GridLength.Auto };
            grid.RowDefinitions.Add(row);
            state.AddedRows.Add(row);
        }

        foreach (UIElement child in grid.Children)
        {
            if (ReferenceEquals(child, button))
            {
                Grid.SetRow(child, 1);
                Grid.SetColumn(child, 0);
                button.HorizontalAlignment = HorizontalAlignment.Left;
                button.Margin = new Thickness(0, 8, 0, 0);
            }
            else
            {
                Grid.SetRow(child, 0);
                Grid.SetColumn(child, 0);
            }
        }
    }

    private static void AdaptHeaderGrid(Grid grid, bool veryCompact)
    {
        if (grid.ColumnDefinitions.Count != 2)
            return;

        var textBlocks = grid.Children.OfType<TextBlock>().ToList();
        if (textBlocks.Count != 2)
            return;

        var right = textBlocks.FirstOrDefault(text => Grid.GetColumn(text) == 1);
        if (right is null)
            return;

        var state = States.GetOrCreateValue(grid);

        if (!veryCompact)
        {
            Restore(grid, state);
            return;
        }

        if (state.Positions.Count == 0)
        {
            foreach (UIElement child in grid.Children)
                state.Positions[child] = (Grid.GetRow(child), Grid.GetColumn(child));
        }

        Grid.SetRow(right, 1);
        Grid.SetColumn(right, 0);
        right.HorizontalAlignment = HorizontalAlignment.Left;
        right.Margin = new Thickness(0, 4, 0, 0);

        if (grid.RowDefinitions.Count < 2)
        {
            var row = new RowDefinition { Height = GridLength.Auto };
            grid.RowDefinitions.Add(row);
            state.AddedRows.Add(row);
        }
    }

    private static void Restore(Grid grid, LayoutState state)
    {
        if (state.Positions.Count == 0)
            return;

        foreach (UIElement child in grid.Children)
        {
            if (!state.Positions.TryGetValue(child, out var position))
                continue;

            Grid.SetRow(child, position.Row);
            Grid.SetColumn(child, position.Column);

            if (child is Button)
                child.ClearValue(FrameworkElement.MarginProperty);
            else if (child is TextBlock)
                child.ClearValue(FrameworkElement.MarginProperty);
        }

        foreach (var row in state.AddedRows)
            grid.RowDefinitions.Remove(row);

        state.AddedRows.Clear();
        state.Positions.Clear();
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root)
        where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T typed)
                yield return typed;

            foreach (var descendant in FindVisualChildren<T>(child))
                yield return descendant;
        }
    }
}
