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

        if (e.OriginalSource is not DependencyObject source)
            return;

        var row = FindVisualParent<DataGridRow>(source);
        if (row?.Item is null)
            return;

        dataGrid.SelectedItem = row.Item;
        row.IsSelected = true;
        dataGrid.Focus();
    }

    private static void OnMainWindowLoadedForModules(object sender, RoutedEventArgs e)
    {
        if (sender is MainWindow window)
            window.BuildModuleNavigation();
    }

    /// <summary>
    /// Construye una navegación real de módulos alrededor de la interfaz existente.
    /// No reubica hijos internos del Grid original; aloja el Grid completo dentro del módulo RED/IP.
    /// </summary>
    private void BuildModuleNavigation()
    {
        if (_moduleNavigationBuilt)
            return;

        if (Content is not Grid originalContent)
            return;

        _moduleNavigationBuilt = true;
        Content = null;

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

        var redTab = new TabItem
        {
            Header = "RED / IP",
            Content = CreateNetworkModuleContent(originalContent)
        };

        var usbTab = CreateUsbModuleTab();

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

        Content = modules;
    }

    /// <summary>
    /// Envuelve la pantalla de RED/IP existente con una consola de modos de descubrimiento.
    /// </summary>
    private Grid CreateNetworkModuleContent(Grid originalContent)
    {
        var networkModule = new Grid
        {
            Background = (Brush)FindResource("BgBrush")
        };

        networkModule.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        networkModule.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var modePanel = new Border
        {
            Background = (Brush)FindResource("PanelBrush"),
            BorderBrush = (Brush)FindResource("BorderBrush2"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5),
            Margin = new Thickness(0, 0, 0, 8),
            Padding = new Thickness(12, 8, 12, 8)
        };

        var modeLayout = new Grid();
        modeLayout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) });
        modeLayout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

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

        var buttonsPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };

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

        Grid.SetRow(originalContent, 1);
        networkModule.Children.Add(originalContent);

        return networkModule;
    }

    /// <summary>
    /// Crea el módulo USB/UVC usando la instancia singleton del servicio local.
    /// </summary>
    private TabItem CreateUsbModuleTab()
    {
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

        _embeddedLocalCameraWindow = new LocalCamerasWindow(service)
        {
            ShowInTaskbar = false
        };

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

        _embeddedLocalCameraWindow.Content = null;
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
