using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CameraInspector.Video;
using Microsoft.Extensions.DependencyInjection;

namespace CameraInspector.App;

/// <summary>
/// Integración de los módulos principales de Camera Inspector.
/// La vista RED/IP existente se conserva y se aloja como módulo; USB/UVC reutiliza la vista local validada.
/// </summary>
public partial class MainWindow
{
    // _embeddedLocalCameraWindow conserva la vista de cámaras UVC mientras permanece alojada en la pestaña principal.
    private LocalCamerasWindow? _embeddedLocalCameraWindow;

    // _moduleNavigationBuilt impide reconstruir la navegación cuando WPF dispara Loaded más de una vez.
    private bool _moduleNavigationBuilt;

    static MainWindow()
    {
        // El clic derecho debe seleccionar primero la fila bajo el cursor para que las acciones trabajen sobre ella.
        EventManager.RegisterClassHandler(
            typeof(DataGrid),
            FrameworkElement.PreviewMouseRightButtonDownEvent,
            new MouseButtonEventHandler(OnDataGridPreviewMouseRightButtonDown));

        // MainWindow_LoadedForModules inicia la construcción después de que el namescope XAML esté completamente creado.
        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnMainWindowLoadedForModules));
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

    private static void OnMainWindowLoadedForModules(object sender, RoutedEventArgs e)
    {
        if (sender is MainWindow window)
        {
            // BuildModuleNavigation se ejecuta una vez cuando el árbol visual y los nombres del XAML están disponibles.
            window.BuildModuleNavigation();
        }
    }

    /// <summary>
    /// Construye una navegación real de módulos alrededor de la interfaz existente.
    /// No reubica hijos internos del Grid original; simplemente aloja el Grid completo dentro del módulo RED/IP.
    /// </summary>
    private void BuildModuleNavigation()
    {
        if (_moduleNavigationBuilt)
            return;

        // originalContent conserva la pantalla RED/IP completa creada por XAML y sus bindings existentes.
        if (Content is not Grid originalContent)
            return;

        _moduleNavigationBuilt = true;

        // Quitamos temporalmente el Grid de la Window para convertirlo en contenido de la primera pestaña.
        Content = null;

        // modules es el selector principal de las áreas funcionales de Camera Inspector.
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

        // redTab conserva sin modificaciones la interfaz que ya utilizamos para descubrir y gestionar cámaras IP.
        var redTab = new TabItem
        {
            Header = "ESCANEO DE RED",
            Content = originalContent
        };

        // usbTab reutiliza el mismo componente UVC que ya demostró captura de vídeo real.
        var usbTab = CreateUsbModuleTab();

        // nvrTab queda preparado para la fase futura sin simular capacidades todavía inexistentes.
        var nvrTab = new TabItem
        {
            Header = "NVR / DVR",
            Content = CreatePendingModuleContent(
                "MÓDULO NVR / DVR",
                "Módulo reservado para descubrimiento de grabadores y administración de canales NVR/DVR.")
        };

        modules.Items.Add(redTab);
        modules.Items.Add(usbTab);
        modules.Items.Add(nvrTab);
        modules.SelectedIndex = 0;

        // La navegación pasa a ser el contenido único de la ventana principal.
        Content = modules;
    }

    /// <summary>
    /// Crea el módulo USB/UVC usando la instancia singleton del servicio local.
    /// </summary>
    private TabItem CreateUsbModuleTab()
    {
        // service comparte la misma instancia de captura que usa la ventana local validada.
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

        // _embeddedLocalCameraWindow construye la vista visual existente sin duplicar su lógica de captura.
        _embeddedLocalCameraWindow = new LocalCamerasWindow(service)
        {
            ShowInTaskbar = false
        };

        // embeddedContent es el árbol visual de la ventana local que ahora será alojado dentro de la pestaña.
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

        // Quitamos el contenido de la Window secundaria antes de asignarlo al TabItem.
        _embeddedLocalCameraWindow.Content = null;

        // RefreshEmbedded fuerza la enumeración porque la ventana secundaria ya no recibirá su propio evento Loaded.
        _embeddedLocalCameraWindow.RefreshEmbedded();

        return new TabItem
        {
            Header = "CÁMARAS USB / UVC",
            Content = embeddedContent
        };
    }

    /// <summary>
    /// Crea el panel informativo de un módulo reservado para una fase posterior.
    /// </summary>
    private Border CreatePendingModuleContent(string title, string description)
    {
        // title es el título del módulo que se muestra al técnico.
        // description explica el alcance futuro sin presentar funciones aún no implementadas.
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
}
