using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace CameraInspector.App.Responsive;

/// <summary>
/// Mantiene las ventanas WPF dentro del área de trabajo disponible, respetando
/// el monitor real y su escalado DPI. El contenido debe aportar sus propios
/// ScrollViewer/DataGrid para el scroll interno.
/// </summary>
public static class ResponsiveWindowBehavior
{
    public static readonly DependencyProperty EnableProperty =
        DependencyProperty.RegisterAttached(
            "Enable",
            typeof(bool),
            typeof(ResponsiveWindowBehavior),
            new PropertyMetadata(false, OnEnableChanged));

    private static readonly DependencyProperty IsHookedProperty =
        DependencyProperty.RegisterAttached(
            "IsHooked",
            typeof(bool),
            typeof(ResponsiveWindowBehavior),
            new PropertyMetadata(false));

    private static readonly DependencyProperty OriginalMinWidthProperty =
        DependencyProperty.RegisterAttached(
            "OriginalMinWidth",
            typeof(double),
            typeof(ResponsiveWindowBehavior),
            new PropertyMetadata(double.NaN));

    private static readonly DependencyProperty OriginalMinHeightProperty =
        DependencyProperty.RegisterAttached(
            "OriginalMinHeight",
            typeof(double),
            typeof(ResponsiveWindowBehavior),
            new PropertyMetadata(double.NaN));

    private static readonly DependencyProperty LastWorkAreaWidthProperty =
        DependencyProperty.RegisterAttached(
            "LastWorkAreaWidth",
            typeof(double),
            typeof(ResponsiveWindowBehavior),
            new PropertyMetadata(double.NaN));

    private static readonly DependencyProperty LastWorkAreaHeightProperty =
        DependencyProperty.RegisterAttached(
            "LastWorkAreaHeight",
            typeof(double),
            typeof(ResponsiveWindowBehavior),
            new PropertyMetadata(double.NaN));

    public static bool GetEnable(DependencyObject element)
        => (bool)element.GetValue(EnableProperty);

    public static void SetEnable(DependencyObject element, bool value)
        => element.SetValue(EnableProperty, value);

    private static bool GetIsHooked(DependencyObject element)
        => (bool)element.GetValue(IsHookedProperty);

    private static void SetIsHooked(DependencyObject element, bool value)
        => element.SetValue(IsHookedProperty, value);

    private static double GetOriginalMinWidth(DependencyObject element)
        => (double)element.GetValue(OriginalMinWidthProperty);

    private static void SetOriginalMinWidth(DependencyObject element, double value)
        => element.SetValue(OriginalMinWidthProperty, value);

    private static double GetOriginalMinHeight(DependencyObject element)
        => (double)element.GetValue(OriginalMinHeightProperty);

    private static void SetOriginalMinHeight(DependencyObject element, double value)
        => element.SetValue(OriginalMinHeightProperty, value);

    private static double GetLastWorkAreaWidth(DependencyObject element)
        => (double)element.GetValue(LastWorkAreaWidthProperty);

    private static void SetLastWorkAreaWidth(DependencyObject element, double value)
        => element.SetValue(LastWorkAreaWidthProperty, value);

    private static double GetLastWorkAreaHeight(DependencyObject element)
        => (double)element.GetValue(LastWorkAreaHeightProperty);

    private static void SetLastWorkAreaHeight(DependencyObject element, double value)
        => element.SetValue(LastWorkAreaHeightProperty, value);

    private static void OnEnableChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is not Window window)
            return;

        if (e.NewValue is true)
        {
            if (!GetIsHooked(window))
            {
                SetOriginalMinWidth(window, window.MinWidth);
                SetOriginalMinHeight(window, window.MinHeight);
                window.Loaded += WindowLoaded;
                window.Closed += WindowClosed;
                window.LocationChanged += WindowLocationChanged;
                window.StateChanged += WindowStateChanged;
                SetIsHooked(window, true);
            }

            ApplyBounds(window);
            return;
        }

        if (GetIsHooked(window))
        {
            window.Loaded -= WindowLoaded;
            window.Closed -= WindowClosed;
            window.SizeChanged -= WindowSizeChanged;
            window.LocationChanged -= WindowLocationChanged;
            window.StateChanged -= WindowStateChanged;
            SetIsHooked(window, false);
        }
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
        window.LocationChanged -= WindowLocationChanged;
        window.StateChanged -= WindowStateChanged;
    }

    private static void WindowSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is Window window)
            ApplyBounds(window);
    }

    private static void WindowLocationChanged(object? sender, EventArgs e)
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

        var workArea = GetCurrentMonitorWorkArea(window);
        const double margin = 24;
        var maxWidth = Math.Max(360, workArea.Width - margin);
        var maxHeight = Math.Max(260, workArea.Height - margin);

        // Conserva los mínimos declarados por cada ventana. Solo se reducen temporalmente
        // cuando el monitor actual no dispone físicamente de ese espacio.
        var originalMinWidth = GetOriginalMinWidth(window);
        var originalMinHeight = GetOriginalMinHeight(window);
        if (double.IsNaN(originalMinWidth))
        {
            originalMinWidth = window.MinWidth;
            SetOriginalMinWidth(window, originalMinWidth);
        }
        if (double.IsNaN(originalMinHeight))
        {
            originalMinHeight = window.MinHeight;
            SetOriginalMinHeight(window, originalMinHeight);
        }

        window.MinWidth = Math.Min(originalMinWidth, maxWidth);
        window.MinHeight = Math.Min(originalMinHeight, maxHeight);
        window.MaxWidth = Math.Max(window.MinWidth, maxWidth);
        window.MaxHeight = Math.Max(window.MinHeight, maxHeight);

        if (window.WindowState == WindowState.Normal)
        {
            if (window.Width > window.MaxWidth)
                window.Width = window.MaxWidth;
            if (window.Height > window.MaxHeight)
                window.Height = window.MaxHeight;
        }

        SetLastWorkAreaWidth(window, workArea.Width);
        SetLastWorkAreaHeight(window, workArea.Height);

        // SizeChanged se conecta una sola vez; ApplyBounds no vuelve a registrar el evento
        // en cada resize, evitando una cadena innecesaria de suscripciones.
        if (!window.SizeChanged?.GetInvocationList().Contains(WindowSizeChanged) ?? true)
            window.SizeChanged += WindowSizeChanged;
    }

    private static Size GetCurrentMonitorWorkArea(Window window)
    {
        try
        {
            var handle = new WindowInteropHelper(window).Handle;
            if (handle != nint.Zero)
            {
                var monitor = MonitorFromWindow(handle, MonitorDefaultToNearest);
                if (monitor != nint.Zero)
                {
                    var info = new MonitorInfo
                    {
                        CbSize = Marshal.SizeOf<MonitorInfo>()
                    };

                    if (GetMonitorInfo(monitor, ref info))
                    {
                        var dpi = VisualTreeHelper.GetDpi(window);
                        var width = (info.Work.Right - info.Work.Left) / Math.Max(0.1, dpi.DpiScaleX);
                        var height = (info.Work.Bottom - info.Work.Top) / Math.Max(0.1, dpi.DpiScaleY);
                        return new Size(width, height);
                    }
                }
            }
        }
        catch
        {
            // Fallback para entornos donde user32/DPI no esté disponible como se espera.
        }

        return SystemParameters.WorkArea.Size;
    }

    private const uint MonitorDefaultToNearest = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int CbSize;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
    }

    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint hwnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo monitorInfo);
}
