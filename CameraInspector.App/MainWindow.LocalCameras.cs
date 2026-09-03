using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CameraInspector.Core.Interfaces;
using CameraInspector.App.ViewModels;
using CameraInspector.Video;
using Microsoft.Extensions.DependencyInjection;

namespace CameraInspector.App;

/// <summary>
/// Integración de los módulos principales de Camera Inspector.
/// La vista IP se organiza en detección por red y detección directa; USB/UVC reutiliza la vista local.
/// </summary>
public partial class MainWindow
{
    private LocalCamerasWindow? _embeddedLocalCameraWindow;
    private bool _moduleNavigationBuilt;

    private static void OnDataGridPreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not DataGrid dataGrid) return;
        if (e.OriginalSource is not DependencyObject source) return;
        var row = FindVisualParent<DataGridRow>(source);
        if (row?.Item is null) return;
        dataGrid.SelectedItem = row.Item;
        row.IsSelected = true;
        dataGrid.Focus();
    }

    private static void OnMainWindowLoadedForModules(object sender, RoutedEventArgs e)
    {
        if (sender is MainWindow window) window.BuildModuleNavigation();
    }

    private void BuildModuleNavigation()
    {
        if (_moduleNavigationBuilt) return;
        if (Content is not Grid originalContent) return;

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

        modules.Items.Add(new TabItem
        {
            Header = "CÁMARAS IP / RED",
            Content = CreateNetworkModuleContent(originalContent)
        });
        modules.Items.Add(CreateUsbModuleTab());
        modules.Items.Add(new TabItem
        {
            Header = "NVR / DVR",
            Content = CreatePendingModuleContent(
                "MÓDULO NVR / DVR",
                "Módulo reservado para descubrimiento de grabadores y administración de canales NVR/DVR.")
        });

        modules.SelectedIndex = 0;
        Content = modules;
    }

    private Grid CreateNetworkModuleContent(Grid originalContent)
    {
        var networkModule = new Grid { Background = (Brush)FindResource("BgBrush") };
        networkModule.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        networkModule.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        networkModule.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var headerPanel = new Border
        {
            Background = (Brush)FindResource("PanelBrush"),
            BorderBrush = (Brush)FindResource("BorderBrush2"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5),
            Margin = new Thickness(0, 0, 0, 8),
            Padding = new Thickness(12, 8, 12, 8)
        };

        var headerLayout = new Grid();
        headerLayout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
        headerLayout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var titlePanel = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        titlePanel.Children.Add(new TextBlock
        {
            Text = "CÁMARA IP / RED",
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)FindResource("AccentBrush")
        });
        titlePanel.Children.Add(new TextBlock
        {
            Text = "Seleccione cómo desea detectar o acceder a la cámara.",
            Margin = new Thickness(0, 3, 0, 0),
            Foreground = (Brush)FindResource("TextDimBrush")
        });
        titlePanel.Children.Add(new TextBlock
        {
            Text = "La detección directa no reemplaza el escaneo de red.",
            Margin = new Thickness(0, 2, 0, 0),
            Foreground = (Brush)FindResource("TextDimBrush")
        });
        Grid.SetColumn(titlePanel, 0);
        headerLayout.Children.Add(titlePanel);

        var controlsPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };

        controlsPanel.Children.Add(new TextBlock
        {
            Text = "INTERFAZ",
            FontFamily = new FontFamily("Consolas"),
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)FindResource("TextDimBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 7, 0)
        });

        var interfaceSelector = new ComboBox
        {
            Width = 280,
            Height = 32,
            ToolTip = "Interfaz que se utilizará para discovery y escaneo de red."
        };
        interfaceSelector.SetBinding(ItemsControl.ItemsSourceProperty, new System.Windows.Data.Binding("AvailableInterfaces"));
        interfaceSelector.SetBinding(Selector.SelectedItemProperty, new System.Windows.Data.Binding("SelectedInterface")
        {
            Mode = System.Windows.Data.BindingMode.TwoWay
        });
        controlsPanel.Children.Add(interfaceSelector);

        var globalScanButton = new Button
        {
            Content = "▣ ESCANEAR RED",
            Width = 145,
            Height = 32,
            Margin = new Thickness(8, 0, 0, 0),
            Style = (Style)FindResource("PrimaryButton"),
            ToolTip = "Ejecuta la detección principal sobre la interfaz seleccionada."
        };
        globalScanButton.SetBinding(Button.CommandProperty, new System.Windows.Data.Binding("ScanCommand"));
        controlsPanel.Children.Add(globalScanButton);

        Grid.SetColumn(controlsPanel, 1);
        headerLayout.Children.Add(controlsPanel);
        headerPanel.Child = headerLayout;
        Grid.SetRow(headerPanel, 0);
        networkModule.Children.Add(headerPanel);

        var modePanel = new Border
        {
            Background = (Brush)FindResource("PanelBrush"),
            BorderBrush = (Brush)FindResource("BorderBrush2"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5),
            Margin = new Thickness(0, 0, 0, 8)
        };

        var modes = new TabControl
        {
            Background = (Brush)FindResource("PanelBrush"),
            Foreground = (Brush)FindResource("TextBrush"),
            BorderBrush = (Brush)FindResource("BorderBrush2"),
            BorderThickness = new Thickness(0)
        };

        var networkTab = new TabItem { Header = "POR RED" };
        var networkTabContent = new StackPanel { Margin = new Thickness(12), Orientation = Orientation.Horizontal };
        networkTabContent.Children.Add(new Border
        {
            Background = (Brush)FindResource("Panel2Brush"),
            BorderBrush = (Brush)FindResource("BorderBrush2"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10),
            Margin = new Thickness(0, 0, 10, 0),
            Child = new StackPanel
            {
                Children =
                {
                    new TextBlock { Text = "DETECCIÓN POR RED", FontFamily = new FontFamily("Consolas"), FontWeight = FontWeights.Bold, Foreground = (Brush)FindResource("AccentBrush") },
                    new TextBlock { Text = "Descubre cámaras dentro de la subred o en todas las interfaces activas.", Margin = new Thickness(0, 4, 0, 0), Foreground = (Brush)FindResource("TextDimBrush"), TextWrapping = TextWrapping.Wrap, Width = 420 }
                }
            }
        });

        var subnetButton = new Button
        {
            Content = "ESCANEAR SUBRED",
            Width = 155,
            Height = 38,
            Margin = new Thickness(0, 0, 8, 0),
            Style = (Style)FindResource("SecondaryButton"),
            ToolTip = "Escanear la subred asociada a la interfaz seleccionada."
        };
        subnetButton.SetBinding(Button.CommandProperty, new System.Windows.Data.Binding("ScanNetworkSubnetCommand"));
        networkTabContent.Children.Add(subnetButton);

        var fullButton = new Button
        {
            Content = "ESCANEO TOTAL",
            Width = 135,
            Height = 38,
            Margin = new Thickness(0, 0, 8, 0),
            Style = (Style)FindResource("SecondaryButton"),
            ToolTip = "Recorrer todas las interfaces de red activas y consolidar cámaras sin duplicados."
        };
        fullButton.SetBinding(Button.CommandProperty, new System.Windows.Data.Binding("ScanFullNetworkCommand"));
        networkTabContent.Children.Add(fullButton);
        networkTab.Content = networkTabContent;

        var directTab = new TabItem { Header = "DIRECTA" };
        var directLayout = new StackPanel { Margin = new Thickness(12) };
        directLayout.Children.Add(new TextBlock
        {
            Text = "DETECCIÓN DIRECTA",
            FontFamily = new FontFamily("Consolas"),
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)FindResource("AccentBrush")
        });
        directLayout.Children.Add(new TextBlock
        {
            Text = "Ideal para una cámara conectada directamente, una IP conocida o un enlace APIPA.",
            Margin = new Thickness(0, 4, 0, 10),
            Foreground = (Brush)FindResource("TextDimBrush"),
            TextWrapping = TextWrapping.Wrap
        });

        var directControls = new StackPanel { Orientation = Orientation.Horizontal };
        directControls.Children.Add(new TextBlock
        {
            Text = "IP OBJETIVO",
            FontFamily = new FontFamily("Consolas"),
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)FindResource("TextDimBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 7, 0)
        });

        var targetTextBox = new TextBox
        {
            Width = 170,
            Height = 32,
            FontFamily = new FontFamily("Consolas"),
            ToolTip = "Opcional. Vacío = discovery directo. Ej.: 192.168.1.50 o 169.254.10.20."
        };
        targetTextBox.SetBinding(TextBox.TextProperty, new System.Windows.Data.Binding("DirectCameraIp")
        {
            Mode = System.Windows.Data.BindingMode.TwoWay,
            UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged
        });
        directControls.Children.Add(targetTextBox);

        var directButton = new Button
        {
            Content = "DETECTAR CÁMARA",
            Width = 150,
            Height = 38,
            Margin = new Thickness(8, 0, 0, 0),
            Style = (Style)FindResource("PrimaryButton"),
            ToolTip = "Con IP: limita las pruebas al host indicado. Sin IP: realiza discovery directo y APIPA cuando corresponde."
        };
        directButton.SetBinding(Button.CommandProperty, new System.Windows.Data.Binding("ScanDirectCameraCommand"));
        directControls.Children.Add(directButton);

        var networkConfigButton = new Button
        {
            Content = "CONFIG. RED",
            Width = 120,
            Height = 38,
            Margin = new Thickness(8, 0, 0, 0),
            Style = (Style)FindResource("SecondaryButton"),
            ToolTip = "Abrir configuración de red de la cámara seleccionada. La operación informará claramente si ONVIF no está disponible."
        };
        networkConfigButton.Click += NetworkConfigurationButton_Click;
        directControls.Children.Add(networkConfigButton);

        directLayout.Children.Add(directControls);
        directTab.Content = directLayout;

        modes.Items.Add(networkTab);
        modes.Items.Add(directTab);
        modes.SelectedIndex = 0;
        modePanel.Child = modes;
        Grid.SetRow(modePanel, 1);
        networkModule.Children.Add(modePanel);

        Grid.SetRow(originalContent, 2);
        networkModule.Children.Add(originalContent);
        return networkModule;
    }

    private void NetworkConfigurationButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ViewModels.MainViewModel mainViewModel || mainViewModel.SelectedDevice is null)
            return;

        if (!mainViewModel.SelectedDevice.OnvifSupported)
        {
            ShowInformation(
                "La configuración de red editable requiere que la cámara exponga ONVIF. La detección y el vídeo pueden seguir funcionando aunque esta función no esté disponible.",
                "Camera Inspector — Configuración de red");
            return;
        }

        var onvif = App.Services?.GetService<IOnvifDeviceService>();
        var credentials = App.Services?.GetService<ICredentialStore>();
        var cameraCredentials = App.Services?.GetService<ICameraCredentialStore>();
        if (onvif is null || credentials is null || cameraCredentials is null)
        {
            ShowInformation(
                "No están disponibles los servicios necesarios para configurar la cámara.",
                "Camera Inspector — Configuración de red");
            return;
        }

        var window = new NetworkConfigurationWindow(
            mainViewModel.SelectedDevice,
            onvif,
            credentials,
            cameraCredentials)
        {
            Owner = this,
            ShowInTaskbar = true
        };
        window.ShowDialog();
    }

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

        _embeddedLocalCameraWindow = new LocalCamerasWindow(service) { ShowInTaskbar = false };
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
        return new TabItem { Header = "CÁMARAS USB / UVC", Content = embeddedContent };
    }

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
            if (current is T typed) return typed;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }
}
