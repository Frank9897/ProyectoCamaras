using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace CameraInspector.App.Responsive;

/// <summary>
/// Ajusta una ventana al área de trabajo del monitor sin forzar cambios de tamaño
/// durante un resize manual. El contenido debe aportar sus propios ScrollViewer,
/// DataGrid o ListBox cuando pueda superar el espacio disponible.
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

    private static readonly DependencyProperty LastMonitorProperty =
        DependencyProperty.RegisterAttached(
            "LastMonitor",
            typeof(long),
            typeof(ResponsiveWindowBehavior),
            new PropertyMetadata(0L));

    private static readonly DependencyProperty IsAdjustingProperty =
        DependencyProperty.RegisterAttached(
            "IsAdjusting",
            typeof(bool),
            typeof(ResponsiveWindowBehavior),
            new PropertyMetadata(false));

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

    private static long GetLastMonitor(DependencyObject element)
        => (long)element.GetValue(LastMonitorProperty);

    private static void SetLastMonitor(DependencyObject element, long value)
        => element.SetValue(LastMonitorProperty, value);

    private static bool GetIsAdjusting(DependencyObject element)
        => (bool)element.GetValue(IsAdjustingProperty);

    private static void SetIsAdjusting(DependencyObject element, bool value)
        => element.SetValue(IsAdjustingProperty, value);

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

            ApplyBounds(window, force: true);
            return;
        }

        if (!GetIsHooked(window))
            return;

        window.Loaded -= WindowLoaded;
        window.Closed -= WindowClosed;
        window.LocationChanged -= WindowLocationChanged;
        window.StateChanged -= WindowStateChanged;
        SetIsHooked(window, false);
    }

    private static void WindowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is Window window)
            ApplyBounds(window, force: true);
    }

    private static void WindowClosed(object? sender, EventArgs e)
    {
        if (sender is Window window)
        {
            window.LocationChanged -= WindowLocationChanged;
            window.StateChanged -= WindowStateChanged;
        }
    }

    private static void WindowLocationChanged(object? sender, EventArgs e)
    {
        if (sender is Window window)
            ApplyBounds(window, force: false);
    }

    private static void WindowStateChanged(object? sender, EventArgs e)
    {
        if (sender is Window window && window.WindowState == WindowState.Normal)
            ApplyBounds(window, force: true);
    }

    private static void ApplyBounds(Window window, bool force)
    {
        if (GetIsAdjusting(window))
            return;

        if (!window.IsLoaded && PresentationSource.FromVisual(window) is null)
            return;

        var handle = new WindowInteropHelper(window).Handle;
        var monitor = handle == nint.Zero
            ? nint.Zero
            : MonitorFromWindow(handle, MonitorDefaultToNearest);

        var monitorMetrics = GetCurrentMonitorMetrics(window, monitor);
        var monitorId = monitor.ToInt64();
        if (!force && monitorId != 0 && monitorId == GetLastMonitor(window))
            return;

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

        const double margin = 24;
        var maxWidth = Math.Max(360, monitorMetrics.WorkArea.Width - margin);
        var maxHeight = Math.Max(260, monitorMetrics.WorkArea.Height - margin);

        try
        {
            SetIsAdjusting(window, true);

            window.MinWidth = Math.Min(originalMinWidth, maxWidth);
            window.MinHeight = Math.Min(originalMinHeight, maxHeight);
            window.MaxWidth = Math.Max(window.MinWidth, maxWidth);
            window.MaxHeight = Math.Max(window.MinHeight, maxHeight);

            if (window.WindowState == WindowState.Normal)
            {
                FitInitialOrOversizedWindow(window, maxWidth, maxHeight);
                ClampWindowToWorkArea(window, monitorMetrics.WorkArea);
            }

            if (monitorId != 0)
                SetLastMonitor(window, monitorId);
        }
        finally
        {
            SetIsAdjusting(window, false);
        }
    }

    private static void FitInitialOrOversizedWindow(Window window, double maxWidth, double maxHeight)
    {
        var width = window.Width;
        var height = window.Height;

        if (double.IsNaN(width) || double.IsInfinity(width) || width <= 0)
            width = Math.Min(maxWidth, Math.Max(window.MinWidth, 1000));
        if (double.IsNaN(height) || double.IsInfinity(height) || height <= 0)
            height = Math.Min(maxHeight, Math.Max(window.MinHeight, 700));

        if (width <= maxWidth && height <= maxHeight)
            return;

        // Cuando el tamaño inicial no entra, reduce ambos ejes conservando la
        // relación de aspecto para evitar ventanas deformadas o saltos bruscos.
        var scaleX = maxWidth / width;
        var scaleY = maxHeight / height;
        var scale = Math.Min(scaleX, scaleY);

        if (scale >= 1)
            return;

        var newWidth = Math.Max(window.MinWidth, width * scale);
        var newHeight = Math.Max(window.MinHeight, height * scale);

        // El mínimo puede impedir mantener exactamente la relación de aspecto.
        // En ese caso ajustamos el eje restante al máximo que permite el monitor.
        if (newWidth > maxWidth)
            newWidth = maxWidth;
        if (newHeight > maxHeight)
            newHeight = maxHeight;

        window.Width = newWidth;
        window.Height = newHeight;
    }

    private static void ClampWindowToWorkArea(Window window, Rect workArea)
    {
        var width = double.IsNaN(window.Width) || double.IsInfinity(window.Width)
            ? window.ActualWidth
            : window.Width;
        var height = double.IsNaN(window.Height) || double.IsInfinity(window.Height)
            ? window.ActualHeight
            : window.Height;

        if (width <= 0 || height <= 0)
            return;

        var minLeft = workArea.Left;
        var minTop = workArea.Top;
        var maxLeft = workArea.Right - width;
        var maxTop = workArea.Bottom - height;

        var left = Math.Clamp(window.Left, minLeft, Math.Max(minLeft, maxLeft));
        var top = Math.Clamp(window.Top, minTop, Math.Max(minTop, maxTop));

        if (!double.IsNaN(left) && !double.IsInfinity(left))
            window.Left = left;
        if (!double.IsNaN(top) && !double.IsInfinity(top))
            window.Top = top;
    }

    private static MonitorMetrics GetCurrentMonitorMetrics(Window window, nint monitor)
    {
        try
        {
            if (monitor != nint.Zero)
            {
                var info = new MonitorInfo
                {
                    CbSize = Marshal.SizeOf<MonitorInfo>()
                };

                if (GetMonitorInfo(monitor, ref info))
                {
                    if (PresentationSource.FromVisual(window) is HwndSource source && source.CompositionTarget is not null)
                    {
                        var fromDevice = source.CompositionTarget.TransformFromDevice;
                        var topLeft = fromDevice.Transform(new Point(info.Work.Left, info.Work.Top));
                        var bottomRight = fromDevice.Transform(new Point(info.Work.Right, info.Work.Bottom));
                        return new MonitorMetrics(new Rect(topLeft, bottomRight));
                    }

                    var dpi = VisualTreeHelper.GetDpi(window);
                    var scaleX = Math.Max(0.1, dpi.DpiScaleX);
                    var scaleY = Math.Max(0.1, dpi.DpiScaleY);
                    var workArea = new Rect(
                        info.Work.Left / scaleX,
                        info.Work.Top / scaleY,
                        (info.Work.Right - info.Work.Left) / scaleX,
                        (info.Work.Bottom - info.Work.Top) / scaleY);

                    return new MonitorMetrics(workArea);
                }
            }
        }
        catch
        {
            // Fallback para entornos donde user32/DPI no esté disponible como se espera.
        }

        return new MonitorMetrics(SystemParameters.WorkArea);
    }

    private readonly record struct MonitorMetrics(Rect WorkArea);

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
