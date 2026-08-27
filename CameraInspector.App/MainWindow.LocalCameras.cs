using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CameraInspector.Video;
using Microsoft.Extensions.DependencyInjection;

namespace CameraInspector.App;

/// <summary>
/// Extensión de MainWindow para integrar los módulos principales en una única pantalla.
/// El módulo USB/UVC reutiliza la misma vista funcional que ya existe como ventana independiente.
/// </summary>
public partial class MainWindow
{
    /// <summary>
    /// Guarda la ventana contenedora de LocalCamerasWindow para conservar viva la vista que fue reparentada.
    /// </summary>
    private LocalCamerasWindow? _embeddedLocalCameraWindow;

    static MainWindow()
    {
        // El clic derecho debe seleccionar primero la fila bajo el cursor para que las acciones trabajen sobre ella.
        EventManager.RegisterClassHandler(
            typeof(DataGrid),
            FrameworkElement.PreviewMouseRightButtonDownEvent,
            new MouseButtonEventHandler(OnDataGridPreviewMouseRightButtonDown));

        // Cuando MainWindow termina de cargarse, convertimos su contenido actual en el módulo RED / IP.
        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnMainWindowLoaded));
    }

    private static void OnDataGridPreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not DataGrid dataGrid)
            return;

        // originalSource es el elemento exacto bajo el cursor durante el clic derecho.
        if (e.OriginalSource is not DependencyObject source)
            return;

        // row es la fila real de DataGrid que contiene el elemento pulsado.
        var row = FindVisualParent<DataGridRow>(source);
        if (row?.Item is null)
            return;

        // SelectedItem sincroniza la fila con MainViewModel.SelectedDevice antes de ejecutar una acción contextual.
        dataGrid.SelectedItem = row.Item;
        row.IsSelected = true;
        dataGrid.Focus();
    }

    private static void OnMainWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not MainWindow window)
            return;

        // DispatcherPriority.Loaded permite que todos los controles XAML existan antes de reparentarlos.
        window.Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            new Action(() => window.BuildModuleNavigation()));
    }

    private void BuildModuleNavigation()
    {
        // root es el Grid raíz declarado en MainWindow.xaml.
        if (Content is not Grid root)
            return;

        // Evitamos reconstruir módulos si Loaded vuelve a dispararse.
        if (root.Children.OfType<TabControl>().Any())
            return;

        // oldChildren representa el contenido completo de la pantalla IP ya existente.
        var oldChildren = root.Children.Cast<UIElement>().ToList();
        // oldRowDefinitions conserva las alturas y mínimos del layout técnico actual.
        var oldRowDefinitions = root.RowDefinitions
            .Select(row => new RowDefinition
            {
                Height = row.Height,
                MinHeight = row.MinHeight,
                MaxHeight = row.MaxHeight
            })
            .ToList();

        // networkRoot pasa a ser el contenido de la pestaña RED / IP.
        var networkRoot = new Grid();
        foreach (var rowDefinition in oldRowDefinitions)
            networkRoot.RowDefinitions.Add(rowDefinition);

        // Al mover los hijos conservamos Grid.Row/Grid.Column y todos sus bindings.
        foreach (var child in oldChildren)
            networkRoot.Children.Add(child);

        // El root queda reservado al selector superior de módulos.
        root.Children.Clear();
        root.RowDefinitions.Clear();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        // modules centraliza las áreas funcionales de Camera Inspector.
        var modules = new TabControl
        {
            Background = (Brush)FindResource("BgBrush"),
            BorderBrush = (Brush)FindResource("BorderBrush2"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch
        };

        // redTab encapsula la funcionalidad IP existente sin modificar su ViewModel.
        var redTab = new TabItem
        {
            Header = "RED / IP",
            Content = networkRoot
        };

        // usbTab reutiliza el módulo local que ya validaste con la webcam real.
        var usbTab = CreateUsbModuleTab();

        // nvrTab reserva explícitamente el módulo futuro de grabadores.
        var nvrTab = new TabItem
        {
            Header = "NVR / DVR",
            IsEnabled = false,
            Content = CreatePendingModuleContent(
                "MÓDULO NVR / DVR",
                "Reservado para canales de NVR/DVR. Se habilitará cuando incorporemos descubrimiento y gestión de grabadores.")
        };

        modules.Items.Add(redTab);
        modules.Items.Add(usbTab);
        modules.Items.Add(nvrTab);
        modules.SelectedIndex = 0;

        root.Children.Add(modules);
    }

    private TabItem CreateUsbModuleTab()
    {
        // service obtiene la instancia singleton de captura local registrada por DI.
        var service = App.Services?.GetService<LocalCameraService>();
        if (service is null)
        {
            return new TabItem
            {
                Header = "USB / UVC",
                Content = CreatePendingModuleContent(
                    "USB / UVC NO DISPONIBLE",
                    "El servicio de cámaras locales no está registrado en el contenedor de dependencias.")
            };
        }

        // _embeddedLocalCameraWindow inicializa el mismo layout que utilizábamos como ventana auxiliar.
        _embeddedLocalCameraWindow = new LocalCamerasWindow(service);
        var embeddedContent = _embeddedLocalCameraWindow.Content as UIElement;

        if (embeddedContent is null)
        {
            return new TabItem
            {
                Header = "USB / UVC",
                Content = CreatePendingModuleContent(
                    "USB / UVC NO DISPONIBLE",
                    "No fue posible obtener el contenido visual del módulo local.")
            };
        }

        // Retiramos el contenido de la Window para convertirlo en contenido de la pestaña.
        _embeddedLocalCameraWindow.Content = null;
        // La ventana original no recibirá Loaded al quedar embebida, por eso inicializamos la enumeración explícitamente.
        _embeddedLocalCameraWindow.RefreshEmbedded();

        return new TabItem
        {
            Header = "USB / UVC",
            Content = embeddedContent
        };
    }

    private Border CreatePendingModuleContent(string title, string description)
    {
        // title representa el nombre del módulo reservado.
        // description explica al técnico por qué todavía no está habilitado.
        return new Border
        {
            Background = (Brush)FindResource("PanelBrush"),
            BorderBrush = (Brush)FindResource("BorderBrush2"),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(10),
            Padding = new Thickness(18),
            Child = new StackPanel
            {
                Children =
                {
                    new TextBlock
                    {
                        Text = title,
                        FontFamily = new FontFamily("Consolas"),
                        FontSize = 14,
                        FontWeight = FontWeights.Bold,
                        Foreground = (Brush)FindResource("AccentBrush")
                    },
                    new TextBlock
                    {
                        Text = description,
                        Margin = new Thickness(0, 10, 0, 0),
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
