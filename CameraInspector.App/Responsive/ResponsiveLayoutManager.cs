using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CameraInspector.App.Responsive;

/// <summary>
/// Motor de composición responsive de la aplicación.
/// Adapta los bloques existentes sin depender de tamaños fijos para que las
/// futuras secciones puedan reutilizar el mismo comportamiento.
/// </summary>
internal static class ResponsiveLayoutManager
{
    private sealed class LayoutState
    {
        public readonly Dictionary<UIElement, (int Row, int Column, Thickness Margin)> Positions = new();
        public readonly List<RowDefinition> AddedRows = new();
        public readonly List<ColumnDefinition> AddedColumns = new();
    }

    private static readonly ConditionalWeakTable<Grid, LayoutState> States = new();

    public static void Apply(Window window)
    {
        if (!window.IsLoaded || window.Content is not DependencyObject root)
            return;

        var width = window.ActualWidth;
        var height = window.ActualHeight;
        var compact = width < 1120;
        var veryCompact = width < 900;
        var shortWindow = height < 760;
        var veryShort = height < 650;

        foreach (var grid in FindVisualChildren<Grid>(root))
        {
            AdaptAdapterGrid(grid, veryCompact);
            AdaptNetworkSummaryGrid(grid, veryCompact);
            AdaptActionGrid(grid, compact);
            AdaptHeaderGrid(grid, veryCompact);
        }

        foreach (var tabControl in FindVisualChildren<TabControl>(root))
            AdaptTabControl(tabControl, compact, shortWindow);

        // La tabla y el detalle son las dos zonas que deben absorber el espacio
        // restante. Sus mínimos bajan cuando la ventana es pequeña para evitar
        // que las secciones superiores queden recortadas.
        foreach (var dataGrid in FindVisualChildren<DataGrid>(root))
        {
            dataGrid.MinColumnWidth = 55;
            dataGrid.MinHeight = veryShort ? 90 : shortWindow ? 110 : 130;
        }

        foreach (var border in FindVisualChildren<Border>(root))
        {
            if (border.Child is not Grid grid || grid.RowDefinitions.Count < 2)
                continue;

            if (border.MinHeight >= 190)
                border.MinHeight = veryShort ? 125 : shortWindow ? 150 : 180;
            else if (border.MinHeight >= 160)
                border.MinHeight = veryShort ? 90 : shortWindow ? 110 : 130;
        }

        // MainWindow usa seis filas: encabezado, adaptador, perfiles, tabla,
        // detalle y estado. Las dos zonas grandes reciben todo el espacio libre.
        var rootGrid = root as Grid ?? FindVisualChildren<Grid>(root).FirstOrDefault();
        if (rootGrid is not null && rootGrid.RowDefinitions.Count == 6)
        {
            rootGrid.RowDefinitions[3].Height = new GridLength(1, GridUnitType.Star);
            rootGrid.RowDefinitions[4].Height = new GridLength(1, GridUnitType.Star);
            rootGrid.RowDefinitions[3].MinHeight = veryShort ? 90 : shortWindow ? 110 : 130;
            rootGrid.RowDefinitions[4].MinHeight = veryShort ? 105 : shortWindow ? 130 : 160;
        }
    }

    private static void AdaptAdapterGrid(Grid grid, bool veryCompact)
    {
        if (grid.ColumnDefinitions.Count != 3 || grid.RowDefinitions.Count != 0)
            return;

        var combo = grid.Children.OfType<ComboBox>().FirstOrDefault();
        var button = grid.Children.OfType<Button>().FirstOrDefault();
        var label = grid.Children.OfType<StackPanel>().FirstOrDefault();
        if (combo is null || button is null || label is null)
            return;

        var state = States.GetOrCreateValue(grid);

        if (!veryCompact)
        {
            Restore(grid, state);
            return;
        }

        Capture(grid, state);
        EnsureRow(grid, state, 0);
        EnsureRow(grid, state, 1);

        Grid.SetRow(label, 0);
        Grid.SetColumn(label, 0);
        Grid.SetColumnSpan(label, 2);
        label.Margin = new Thickness(0, 0, 0, 6);

        Grid.SetRow(combo, 1);
        Grid.SetColumn(combo, 0);
        Grid.SetColumnSpan(combo, 2);
        combo.HorizontalAlignment = HorizontalAlignment.Stretch;
        combo.MinWidth = 0;
        combo.MaxWidth = double.PositiveInfinity;

        Grid.SetRow(button, 1);
        Grid.SetColumn(button, 2);
        button.HorizontalAlignment = HorizontalAlignment.Left;
        button.Margin = new Thickness(10, 0, 0, 0);

        grid.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
        grid.ColumnDefinitions[1].Width = new GridLength(1, GridUnitType.Star);
        grid.ColumnDefinitions[2].Width = GridLength.Auto;
    }

    private static void AdaptNetworkSummaryGrid(Grid grid, bool veryCompact)
    {
        if (grid.ColumnDefinitions.Count != 4 || grid.RowDefinitions.Count != 2)
            return;

        var textBlocks = grid.Children.OfType<TextBlock>().ToList();
        if (textBlocks.Count < 8)
            return;

        var state = States.GetOrCreateValue(grid);
        if (!veryCompact)
        {
            Restore(grid, state);
            return;
        }

        Capture(grid, state);
        while (grid.RowDefinitions.Count < 4)
            EnsureRow(grid, state, grid.RowDefinitions.Count);

        foreach (var text in textBlocks)
        {
            var originalColumn = state.Positions.TryGetValue(text, out var position) ? position.Column : Grid.GetColumn(text);
            var originalRow = state.Positions.TryGetValue(text, out position) ? position.Row : Grid.GetRow(text);
            var isValue = originalColumn is 1 or 3;
            var pair = originalColumn >= 2 ? 1 : 0;
            var row = pair * 2 + (originalRow == 1 ? 1 : 0);

            Grid.SetRow(text, row);
            Grid.SetColumn(text, isValue ? 1 : 0);
            Grid.SetColumnSpan(text, 1);
            text.Margin = new Thickness(0, row > 0 ? 3 : 0, isValue ? 0 : 7, 0);
        }

        grid.ColumnDefinitions[0].Width = GridLength.Auto;
        grid.ColumnDefinitions[1].Width = new GridLength(1, GridUnitType.Star);
        grid.ColumnDefinitions[2].Width = new GridLength(0);
        grid.ColumnDefinitions[3].Width = new GridLength(0);
    }

    private static void AdaptActionGrid(Grid grid, bool compact)
    {
        if (grid.ColumnDefinitions.Count < 2 || grid.ColumnDefinitions.Count > 3)
            return;

        // Los perfiles tienen su propia adaptación para evitar que esta rutina
        // y AdaptTabControl compitan por las mismas filas.
        if (IsScanProfileGrid(grid))
            return;

        var button = grid.Children.OfType<Button>().FirstOrDefault(child => Grid.GetColumn(child) > 0);
        if (button is null)
            return;

        var state = States.GetOrCreateValue(grid);
        if (!compact)
        {
            Restore(grid, state);
            return;
        }

        Capture(grid, state);
        EnsureRow(grid, state, 1);

        foreach (var child in grid.Children.OfType<FrameworkElement>())
        {
            if (ReferenceEquals(child, button))
            {
                Grid.SetRow(child, 1);
                Grid.SetColumn(child, 0);
                Grid.SetColumnSpan(child, grid.ColumnDefinitions.Count);
                child.HorizontalAlignment = HorizontalAlignment.Left;
                child.Margin = new Thickness(0, 8, 0, 0);
            }
            else
            {
                Grid.SetRow(child, 0);
                Grid.SetColumn(child, 0);
                Grid.SetColumnSpan(child, grid.ColumnDefinitions.Count);
            }
        }
    }

    private static bool IsScanProfileGrid(Grid grid)
    {
        var stack = grid.Children.OfType<StackPanel>().FirstOrDefault();
        if (stack is null)
            return false;

        return stack.Children.OfType<TextBlock>().Any(text =>
            string.Equals(text.Text, "UNA SOLA IP", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(text.Text, "SUBRED DEL ADAPTADOR", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(text.Text, "TODOS LOS ADAPTADORES ACTIVOS", StringComparison.OrdinalIgnoreCase));
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

        Capture(grid, state);
        EnsureRow(grid, state, 1);
        Grid.SetRow(right, 1);
        Grid.SetColumn(right, 0);
        right.HorizontalAlignment = HorizontalAlignment.Left;
        right.Margin = new Thickness(0, 4, 0, 0);
    }

    private static void AdaptTabControl(TabControl tabControl, bool compact, bool shortWindow)
    {
        var headers = tabControl.Items
            .OfType<TabItem>()
            .Select(item => item.Header?.ToString())
            .Where(header => header is not null)
            .ToList();

        if (headers.Count == 0)
            return;

        var isProfileTabs = headers.Contains("DIRECTA", StringComparer.OrdinalIgnoreCase)
            && headers.Contains("RED LOCAL", StringComparer.OrdinalIgnoreCase)
            && headers.Contains("RED COMPLETA", StringComparer.OrdinalIgnoreCase);

        if (!isProfileTabs)
            return;

        // Nunca recortar el contenido del perfil por un Height/MaxHeight fijo.
        // La fila superior de MainWindow es Auto y debe crecer con el perfil.
        tabControl.ClearValue(FrameworkElement.HeightProperty);
        tabControl.ClearValue(FrameworkElement.MaxHeightProperty);
        tabControl.MinHeight = 0;
        tabControl.VerticalAlignment = VerticalAlignment.Stretch;

        foreach (var tab in tabControl.Items.OfType<TabItem>())
        {
            if (tab.Content is not Grid grid)
                continue;

            var button = grid.Children.OfType<Button>().FirstOrDefault();
            var content = grid.Children.OfType<StackPanel>().FirstOrDefault();
            if (button is null || content is null)
                continue;

            var state = States.GetOrCreateValue(grid);
            if (!compact)
            {
                Restore(grid, state);
                continue;
            }

            Capture(grid, state);
            EnsureRow(grid, state, 1);

            Grid.SetRow(content, 0);
            Grid.SetColumn(content, 0);
            Grid.SetColumnSpan(content, 2);
            content.Margin = new Thickness(0);
            content.VerticalAlignment = VerticalAlignment.Top;

            Grid.SetRow(button, 1);
            Grid.SetColumn(button, 0);
            Grid.SetColumnSpan(button, 2);
            button.HorizontalAlignment = HorizontalAlignment.Left;
            button.Margin = new Thickness(0, 8, 0, 0);
        }
    }

    private static void Capture(Grid grid, LayoutState state)
    {
        if (state.Positions.Count != 0)
            return;

        foreach (UIElement child in grid.Children)
            state.Positions[child] = (Grid.GetRow(child), Grid.GetColumn(child), child is FrameworkElement element ? element.Margin : default);
    }

    private static void EnsureRow(Grid grid, LayoutState state, int index)
    {
        while (grid.RowDefinitions.Count <= index)
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
            Grid.SetColumnSpan(child, 1);
            if (child is FrameworkElement element)
                element.Margin = position.Margin;
        }

        foreach (var row in state.AddedRows)
            grid.RowDefinitions.Remove(row);

        state.AddedRows.Clear();
        state.AddedColumns.Clear();
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
