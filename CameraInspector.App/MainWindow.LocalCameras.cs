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

        // redTab contiene toda la funcionalidad de cámaras IP, incluida la nueva consola de modos de discovery.
        var redTab = new TabItem
        {
            Header = "RED / IP",
            Content = CreateNetworkModuleContent(originalContent)
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
    /// Envuelve la pantalla de RED/IP existente con una consola de modos de descubrimiento.
    /// </summary>
    private Grid CreateNetworkModuleContent(Grid originalContent)
    {
        // networkModule es el contenedor nuevo que mantiene intacta la vista de red y agrega la navegación superior.
        var networkModule = new Grid
        {
            Background = (Brush)FindResource("BgBrush")
        };

        // La primera fila reserva una franja compacta para las tres estrategias de descubrimiento.
        networkModule.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        // La segunda fila ocupa el espacio restante y contiene la pantalla RED/IP original.
        networkModule.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        // modePanel agrupa visualmente las opciones para que el técnico pueda distinguir una detección directa de un barrido.
        var modePanel = new Border
        {
            Background = (Brush)FindResource("PanelBrush"),
            BorderBrush = (Brush)FindResource("BorderBrush2"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5),
            Margin = new Thickness(0, 0, 0, 8),
            Padding = new Thickness(12, 8, 12, 8)
        };

        // modeLayout organiza título, descripción y botones sin ocupar una altura innecesaria.
        var modeLayout = new Grid();
        modeLayout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) });
        modeLayout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // titlePanel explica el propósito de esta sección y no mezcla el término "puerto" con "puerto de cámara".
        var titlePanel = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center
        };
        titlePanel.Children.Add(new TextBlock
        {
            Text = "MODO DE DESCUBRIMIENTO",
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)FindResource("AccentBrush")
        });
        titlePanel.Children.Add(new TextBlock
        {
            Text = "Elegí cuánto querés buscar.",
            Margin = new Thickness(0, 3, 0, 0),
            Foreground = (Brush)FindResource("TextDimBrush")
        });
        Grid.SetColumn(titlePanel, 0);
        modeLayout.Children.Add(titlePanel);

        // buttonsPanel mantiene las tres acciones en una sola línea para que sean visibles al abrir la aplicación.
        var buttonsPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        // directButton inicia únicamente discovery sobre la interfaz seleccionada, sin ping sweep de la subred.
        var directButton = new Button
        {
            Content = "CÁMARA DIRECTA",
            Width = 145,
            Height = 38,
            Margin = new Thickness(6, 0, 0, 0),
            Style = (Style)FindResource("PrimaryButton"),
            ToolTip = "Detectar una cámara conectada directamente al puerto Ethernet seleccionado."
        };
        directButton.SetBinding(Button.CommandProperty, new System.Windows.Data.Binding("ScanDirectCameraCommand"));
        buttonsPanel.Children.Add(directButton);

        // subnetButton ejecuta el barrido de la subred de la interfaz actualmente seleccionada.
        var subnetButton = new Button
        {
            Content = "ESCANEAR SUBRED",
            Width = 150,
            Height = 38,
            Margin = new Thickness(6, 0, 0, 0),
            Style = (Style)FindResource("SecondaryButton"),
            ToolTip = "Escanear la subred asociada a la interfaz seleccionada."
        };
        subnetButton.SetBinding(Button.CommandProperty, new System.Windows.Data.Binding("ScanNetworkSubnetCommand"));
        buttonsPanel.Children.Add(subnetButton);

        // fullButton recorre todas las interfaces activas de forma secuencial y consolida los resultados.
        var fullButton = new Button
        {
            Content = "ESCANEO TOTAL",
            Width = 130,
            Height = 38,
            Margin = new Thickness(6, 0, 0, 0),
            Style = (Style)FindResource("SecondaryButton"),
            ToolTip = "Recorrer todas las interfaces de red activas y consolidar cámaras sin duplicados."
        };
        fullButton.SetBinding(Button.CommandProperty, new System.Windows.Data.Binding("ScanFullNetworkCommand"));
        buttonsPanel.Children.Add(fullButton);

        Grid.SetColumn(buttonsPanel, 1);
        modeLayout.Children.Add(buttonsPanel);
        modePanel.Child = modeLayout;
        Grid.SetRow(modePanel, 0);
        networkModule.Children.Add(modePanel);

        // Renombramos el botón antiguo del layout existente para que no haya dos acciones con el mismo nombre.
        var legacyScanButton = FindButtonByContent(originalContent, "▣ ESCANEAR RED");
        if (legacyScanButton is not null)
        {
            legacyScanButton.Content = "▣ ESCANEAR SUBRED";
            legacyScanButton.ToolTip = "Escanear la subred de la interfaz seleccionada.";
            legacyScanButton.SetBinding(Button.CommandProperty, new System.Windows.Data.Binding("ScanNetworkSubnetCommand"));
        }

        // originalContent conserva toda la interfaz de resultados, detalle, diagnóstico y video.
        Grid.SetRow(originalContent, 1);
        networkModule.Children.Add(originalContent);

        return networkModule;
    }

    /// <summary>
    /// Busca recursivamente un botón por su texto original dentro de la vista RED/IP.
    /// </summary>
    private static Button? FindButtonByContent(DependencyObject root, string content)
    {
        // childrenCount representa la cantidad de hijos visuales que debemos recorrer.
        var childrenCount = VisualTreeHelper.GetChildrenCount(root);

        for (var index = 0; index < childrenCount; index++)
        {
            // child es el control actual que estamos inspeccionando.
            var child = VisualTreeHelper.GetChild(root, index);

            if (child is Button button && string.Equals(button.Content?.ToString(), content, StringComparison.Ordinal))
                return button;

            // nestedButton permite continuar la búsqueda dentro de contenedores anidados.
            var nestedButton = FindButtonByContent(child, content);
            if (nestedButton is not null)
                return nestedButton;
        }

        return null;
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
