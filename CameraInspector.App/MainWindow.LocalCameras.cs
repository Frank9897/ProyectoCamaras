using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CameraInspector.Video;
using Microsoft.Extensions.DependencyInjection;

namespace CameraInspector.App;

/// <summary>
/// Integración de los módulos principales de Camera Inspector.
/// La red IP conserva su vista existente y el módulo USB/UVC reutiliza la vista local ya validada.
/// </summary>
public partial class MainWindow
{
    // _embeddedLocalCameraWindow conserva la instancia visual del módulo UVC mientras permanece dentro de MainWindow.
    private LocalCamerasWindow? _embeddedLocalCameraWindow;

    // _moduleNavigationBuilt evita reconstruir el contenedor si WPF dispara Loaded más de una vez.
    private bool _moduleNavigationBuilt;

    static MainWindow()
    {
        // El clic derecho debe seleccionar primero la fila bajo el cursor para que las acciones trabajen sobre ella.
        EventManager.RegisterClassHandler(
            typeof(DataGrid),
            FrameworkElement.PreviewMouseRightButtonDownEvent,
            new MouseButtonEventHandler(OnDataGridPreviewMouseRightButtonDown));
    }

    private static void OnDataGridPreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not DataGrid dataGrid)
            return;

        // originalSource es el elemento exacto bajo el cursor durante el clic derecho.
        if (e.OriginalSource is not DependencyObject source)
            return;

        // row representa la fila real que contiene el elemento pulsado.
        var row = FindVisualParent<DataGridRow>(source);
        if (row?.Item is null)
            return;

        // SelectedItem sincroniza la fila con MainViewModel.SelectedDevice antes de ejecutar una acción contextual.
        dataGrid.SelectedItem = row.Item;
        row.IsSelected = true;
        dataGrid.Focus();
    }

    /// <summary>
    /// Construye una navegación real de módulos alrededor de la interfaz existente.
    /// No reubica hijos internos del Grid original, evitando errores de reparenting de WPF.
    /// </summary>
    private void BuildModuleNavigation()
    {
        if (_moduleNavigationBuilt)
            return;

        // originalContent conserva la pantalla RED/IP completa ya construida por XAML.
        if (Content is not Grid originalContent)
            return;

        _moduleNavigationBuilt = true;

        // Quitamos temporalmente el Grid de la Window para poder alojarlo dentro del primer módulo.
        Content = null;

        // modules es el selector visible de los grandes módulos funcionales de Camera Inspector.
        var modules = new TabControl
        {
            Background = (Brush)FindResource("BgBrush"),
            Foreground = (Brush)FindResource("TextBrush"),
            BorderBrush = (Brush)FindResource("BorderBrush2"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch
        };

        // redTab conserva íntegramente la pantalla de descubrimiento, detalle, video y diagnóstico existente.
        var redTab = new TabItem
        {
            Header = "ESCANEO DE RED",
            Content = originalContent
        };

        // usbTab reutiliza la implementación UVC ya validada con la webcam real.
        var usbTab = CreateUsbModuleTab();

        // nvrTab queda disponible como módulo reservado; no se simula funcionalidad inexistente.
        var nvrTab = new TabItem
        {
            Header = "NVR / DVR",
            Content = CreatePendingModuleContent(
                "MÓDULO NVR / DVR",
                "Módulo reservado. Aquí se incorporarán posteriormente descubrimiento de grabadores y canales NVR/DVR.")
        };

        modules.Items.Add(redTab);
        modules.Items.Add(usbTab);
        modules.Items.Add(nvrTab);
        modules.SelectedIndex = 0;

        // La navegación pasa a ser el contenido único y estable de la ventana principal.
        Content = modules;
    }

    /// <summary>
    /// Obtiene la vista del módulo USB/UVC ya existente y la aloja dentro de una pestaña.
    /// </summary>
    private TabItem CreateUsbModuleTab()
    {
        // service reutiliza el singleton de captura local registrado en DI.
        var service = App.Services?.GetService<LocalCameraService>();

        if (service is null)
        {
            return new TabItem
            {
                Header = "CÁMARAS USB / UVC",
                Content = CreatePendingModuleContent(
                    "USB / UVC NO DISPONIBLE",
                    "El servicio de cámaras locales no está registrado en el contenedor de dependencias.")
            };
        }

        // _embeddedLocalCameraWindow crea la vista conocida que ya funciona con la webcam.
        _embeddedLocalCameraWindow = new LocalCamerasWindow(service)
        {
            ShowInTaskbar = false
        };

        // embeddedContent es el Grid visual interno de LocalCamerasWindow que ahora pasa a pertenecer a la pestaña.
        var embeddedContent = _embeddedLocalCameraWindow.Content as UIElement;
        if (embeddedContent is null)
        {
            return new TabItem
            {
                Header = "CÁMARAS USB / UVC",
                Content = CreatePendingModuleContent(
                    "USB / UVC NO DISPONIBLE",
                    "No fue posible crear el contenido visual del módulo local.")
            };
        }

        // Quitamos el contenido de la Window secundaria antes de asignarlo como hijo de TabItem.
        _embeddedLocalCameraWindow.Content = null;

        // RefreshEmbedded dispara la enumeración sin depender de que la ventana secundaria sea visible.
        _embeddedLocalCameraWindow.RefreshEmbedded();

        return new TabItem
        {
            Header = "CÁMARAS USB / UVC",
            Content = embeddedContent
        };
    }

    /// <summary>
    /// Crea el contenido informativo de un módulo todavía no implementado.
    /// </summary>
    private Border CreatePendingModuleContent(string title, string description)
    {
        // title es el título visible del módulo reservado.
        // description explica qué funcionalidad se incorporará en una fase posterior.
        return new Border
        {
            Background = (Brush)FindResource("PanelBrush"),
            BorderBrush = (Brush)FindResource("BorderBrush2"),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(12),
            Padding = new Thickness(22),
            Child = new StackPanel
            {
                Children =
                {
                    new TextBlock
                    {
                        Text = title,
                        FontFamily = new FontFamily("Consolas"),
                        FontSize = 16,
                        FontWeight = FontWeights.Bold,
                        Foreground = (Brush)FindResource("AccentBrush")
                    },
                    new TextBlock
                    {
                        Text = description,
                        Margin = new Thickness(0, 12, 0, 0),
                        TextWrapping = TextWrapping.Wrap,
                        Foreground = (Brush)FindResource("TextDimBrush")
                    }
                }
            }
        };
    }

    private static T? FindVisualParent<T>(DependencyObject? element)
        where T : DependencyObject
    {
        // current asciende por el árbol visual hasta encontrar el contenedor solicitado.
        var current = element;
        while (current is not null)
        {
            if (current is T typed)
                return typed;

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private void MainWindow_LoadedForModules(object? sender, RoutedEventArgs e)
    {
        // Dispatcher permite construir los módulos después de que el XAML haya completado el namescope de MainWindow.
        Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            new Action(BuildModuleNavigation));
    }
}
