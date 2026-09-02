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
/// La vista RED/IP existente se conserva y se aloja como módulo; USB/UVC reutiliza la vista local validada.
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
            Header = "RED / IP",
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
        modeLayout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(255) });
        modeLayout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var titlePanel = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        titlePanel.Children.Add(new TextBlock
        {
            Text = "CÁMARA DIRECTA",
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)FindResource("AccentBrush")
        });
        titlePanel.Children.Add(new TextBlock
        {
            Text = "IP opcional · primero discovery, después acceso.",
            Margin = new Thickness(0, 3, 0, 0),
            Foreground = (Brush)FindResource("TextDimBrush")
        });
        titlePanel.Children.Add(new TextBlock
        {
            Text = "Ideal para cámara Ethernet directa o APIPA.",
            Margin = new Thickness(0, 2, 0, 0),
            Foreground = (Brush)FindResource("TextDimBrush")
        });
        Grid.SetColumn(titlePanel, 0);
        modeLayout.Children.Add(titlePanel);

        var controlsPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };

        controlsPanel.Children.Add(new TextBlock
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
            Width = 145,
            Height = 32,
            Padding = new Thickness(7, 5, 7, 4),
            FontFamily = new FontFamily("Consolas"),
            ToolTip = "Opcional. Vacío = descubrir sin conocer la IP. Ej.: 192.168.1.50 o 169.254.10.20."
        };
        targetTextBox.SetBinding(TextBox.TextProperty, new System.Windows.Data.Binding("DirectCameraIp")
        {
            Mode = System.Windows.Data.BindingMode.TwoWay,
            UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged
        });
        controlsPanel.Children.Add(targetTextBox);

        var directButton = new Button
        {
            Content = "DETECTAR CÁMARA",
            Width = 145,
            Height = 38,
            Margin = new Thickness(8, 0, 0, 0),
            Style = (Style)FindResource("PrimaryButton"),
            ToolTip = "Sin IP: utiliza discovery. Con IP: limita las pruebas al host indicado."
        };
        directButton.SetBinding(Button.CommandProperty, new System.Windows.Data.Binding("ScanDirectCameraCommand"));
        controlsPanel.Children.Add(directButton);

        var credentialsButton = new Button
        {
            Content = "CREDENCIALES",
            Width = 115,
            Height = 38,
            Margin = new Thickness(6, 0, 0, 0),
            Style = (Style)FindResource("SecondaryButton"),
            ToolTip = "Con una cámara seleccionada, ingresar o actualizar usuario y contraseña."
        };
        credentialsButton.SetBinding(Button.CommandProperty, new System.Windows.Data.Binding("SaveCredentialsCommand"));
        controlsPanel.Children.Add(credentialsButton);

        var networkConfigButton = new Button
        {
            Content = "CONFIG. RED",
            Width = 110,
            Height = 38,
            Margin = new Thickness(6, 0, 0, 0),
            Style = (Style)FindResource("SecondaryButton"),
            ToolTip = "Administrar IPv4, gateway, nombre, reinicio y restablecimiento de fábrica."
        };
        networkConfigButton.Click += NetworkConfigurationButton_Click;
        networkConfigButton.SetBinding(Button.IsEnabledProperty, new System.Windows.Data.Binding("SelectedDevice.OnvifSupported"));
        controlsPanel.Children.Add(networkConfigButton);

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
        controlsPanel.Children.Add(subnetButton);

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
        controlsPanel.Children.Add(fullButton);

        Grid.SetColumn(controlsPanel, 1);
        modeLayout.Children.Add(controlsPanel);
        modePanel.Child = modeLayout;
        Grid.SetRow(modePanel, 0);
        networkModule.Children.Add(modePanel);

        Grid.SetRow(originalContent, 1);
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
                "La configuración de red editable requiere que la cámara exponga ONVIF.",
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
