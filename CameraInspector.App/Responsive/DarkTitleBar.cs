using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace CameraInspector.App.Responsive;

/// <summary>
/// Fuerza la barra de título nativa de Windows (dibujada por el sistema operativo, NO
/// por WPF) a modo oscuro en TODAS las ventanas de la app, sin excepción.
///
/// FIX (pedido: "no quiero nada en blanco"): el contenido de cada ventana ya usaba
/// DarkTheme.xaml, pero la barra de título es chrome nativo de Windows que WPF no
/// controla — sin este ajuste queda blanca/clara en cualquier PC con tema claro de
/// Windows, sin importar qué tan oscuro esté el resto de la ventana. Se engancha una
/// única vez (desde App.xaml.cs) al evento Loaded de la clase base Window, así que
/// cubre automáticamente cualquier ventana actual o futura sin tener que tocar cada
/// XAML uno por uno.
/// </summary>
internal static class DarkTitleBar
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE_20H1 = 20;
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE_LEGACY = 19;

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);

    public static void Initialize()
    {
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler(OnWindowLoaded));
    }

    private static void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Window window)
            return;

        Apply(window);
    }

    private static void Apply(Window window)
    {
        try
        {
            var handle = new WindowInteropHelper(window).Handle;
            if (handle == nint.Zero)
                return;

            var enabled = 1;
            // Se intentan ambos valores de atributo porque el número cambió entre builds
            // de Windows 10 (19 en versiones tempranas, 20 desde 20H1 en adelante). Si el
            // sistema operativo no reconoce alguno, DwmSetWindowAttribute simplemente
            // devuelve un código de error que acá se ignora sin romper la ventana.
            DwmSetWindowAttribute(handle, DWMWA_USE_IMMERSIVE_DARK_MODE_20H1, ref enabled, sizeof(int));
            DwmSetWindowAttribute(handle, DWMWA_USE_IMMERSIVE_DARK_MODE_LEGACY, ref enabled, sizeof(int));
        }
        catch
        {
            // Cosmético: si falla (versión de Windows muy vieja, tema de alto contraste,
            // etc.) la app sigue funcionando con la barra de título por defecto del SO.
        }
    }
}
