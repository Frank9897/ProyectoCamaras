using System.Windows;

namespace CameraInspector.App.Responsive;

/// <summary>
/// Mantiene las ventanas WPF dentro del área de trabajo disponible y evita
/// tamaños iniciales imposibles en monitores pequeños o con escalado elevado.
/// El contenido debe aportar sus propios ScrollViewer/DataGrid para el scroll.
/// </summary>
public static class ResponsiveWindowBehavior
{
    public static readonly DependencyProperty EnableProperty =
        DependencyProperty.RegisterAttached(
            "Enable",
            typeof(bool),
            typeof(ResponsiveWindowBehavior),
            new PropertyMetadata(false, OnEnableChanged));

    public static bool GetEnable(DependencyObject element)
        => (bool)element.GetValue(EnableProperty);

    public static void SetEnable(DependencyObject element, bool value)
        => element.SetValue(EnableProperty, value);

    private static readonly DependencyProperty IsHookedProperty =
        DependencyProperty.RegisterAttached(
            "IsHooked",
            typeof(bool),
            typeof(ResponsiveWindowBehavior),
            new PropertyMetadata(false));

    private static bool GetIsHooked(DependencyObject element)
        => (bool)element.GetValue(IsHookedProperty);

    private static void SetIsHooked(DependencyObject element, bool value)
        => element.SetValue(IsHookedProperty, value);

    private static void OnEnableChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is not Window window || e.NewValue is not true)
            return;

        if (!GetIsHooked(window))
        {
            window.Loaded += WindowLoaded;
            window.Closed += WindowClosed;
            SetIsHooked(window, true);
        }

        ApplyBounds(window);
    }

    private static void WindowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is Window window)
            ApplyBounds(window);
    }

    private static void WindowClosed(object? sender, EventArgs e)
    {
        if (sender is not Window window)
            return;

        window.SizeChanged -= WindowSizeChanged;
        window.StateChanged -= WindowStateChanged;
    }

    private static void WindowSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is Window window)
            ApplyBounds(window);
    }

    private static void WindowStateChanged(object? sender, EventArgs e)
    {
        if (sender is Window window && window.WindowState == WindowState.Normal)
            ApplyBounds(window);
    }

    private static void ApplyBounds(Window window)
    {
        if (!window.IsLoaded && PresentationSource.FromVisual(window) is null)
            return;

        var workArea = SystemParameters.WorkArea;
        const double margin = 24;
        var maxWidth = Math.Max(360, workArea.Width - margin);
        var maxHeight = Math.Max(260, workArea.Height - margin);

        window.MaxWidth = maxWidth;
        window.MaxHeight = maxHeight;

        if (window.Width > maxWidth)
            window.Width = maxWidth;
        if (window.Height > maxHeight)
            window.Height = maxHeight;

        // Recalcula límites cuando la ventana cambia de tamaño o monitor.
        window.SizeChanged -= WindowSizeChanged;
        window.StateChanged -= WindowStateChanged;
        window.SizeChanged += WindowSizeChanged;
        window.StateChanged += WindowStateChanged;
    }
}
