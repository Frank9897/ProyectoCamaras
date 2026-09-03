using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Data;

namespace CameraInspector.App;

/// <summary>
/// Agrega el acceso de enlace remoto a la navegación existente sin acoplarlo a VAST,
/// VSS ni a un fabricante concreto.
/// </summary>
public partial class MainWindow
{
    // Se registra antes del constructor para ejecutar después de que el módulo principal
    // haya sido construido por MainWindow.LocalCameras.cs.
    private readonly bool _remoteAccessHook = RegisterRemoteAccessHook();

    private bool RegisterRemoteAccessHook()
    {
        Loaded += (_, _) =>
            Dispatcher.BeginInvoke(new Action(EnsureRemoteAccessTab),
                System.Windows.Threading.DispatcherPriority.Loaded);
        return true;
    }

    private void EnsureRemoteAccessTab()
    {
        if (Content is not TabControl modules || modules.Items.Count == 0)
            return;

        if (modules.Items.OfType<TabItem>().Any(item =>
                string.Equals(item.Header?.ToString(), "CONEXIÓN DE ENLACE", StringComparison.OrdinalIgnoreCase)))
            return;

        var networkModule = modules.Items.OfType<TabItem>().FirstOrDefault(item =>
            string.Equals(item.Header?.ToString(), "CÁMARAS IP / RED", StringComparison.OrdinalIgnoreCase));
        if (networkModule?.Content is not Grid grid)
            return;

        var modes = FindModeTabControl(grid);
        if (modes is null)
            return;

        modes.Items.Add(CreateRemoteAccessTab());
    }

    private static TabControl? FindModeTabControl(DependencyObject root)
    {
        foreach (var child in EnumerateVisualChildren(root))
        {
            if (child is not TabControl tabControl)
                continue;

            if (tabControl.Items.OfType<TabItem>().Any(item =>
                    string.Equals(item.Header?.ToString(), "POR RED", StringComparison.OrdinalIgnoreCase)) &&
                tabControl.Items.OfType<TabItem>().Any(item =>
                    string.Equals(item.Header?.ToString(), "DIRECTA", StringComparison.OrdinalIgnoreCase)))
                return tabControl;
        }
        return null;
    }

    private static IEnumerable<DependencyObject> EnumerateVisualChildren(DependencyObject root)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < count; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            yield return child;
            foreach (var nested in EnumerateVisualChildren(child))
                yield return nested;
        }
    }

    private TabItem CreateRemoteAccessTab()
    {
        var panel = new StackPanel { Margin = new Thickness(16) };

        panel.Children.Add(new TextBlock
        {
            Text = "CONEXIÓN DE ENLACE",
            FontFamily = new FontFamily("Consolas"),
            FontWeight = FontWeights.Bold,
            FontSize = 15,
            Foreground = (Brush)FindResource("AccentBrush")
        });
        panel.Children.Add(new TextBlock
        {
            Text = "Conecte Camera Inspector a otro servicio, VMS, NVR/DVR, proxy o punto de entrada remoto usando solamente host y puerto.",
            Margin = new Thickness(0, 5, 0, 14),
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)FindResource("TextDimBrush")
        });

        var form = new Border
        {
            Background = (Brush)FindResource("Panel2Brush"),
            BorderBrush = (Brush)FindResource("BorderBrush2"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(14)
        };
        var formGrid = new Grid();
        formGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        formGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(230) });
        formGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        formGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        formGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        formGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        formGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        AddRemoteLabel(formGrid, "HOST / SERVIDOR", 0);
        var hostBox = new TextBox
        {
            Height = 32,
            FontFamily = new FontFamily("Consolas"),
            ToolTip = "IP o nombre DNS del equipo de enlace. Ejemplo: 192.168.0.99"
        };
        hostBox.SetBinding(TextBox.TextProperty, new Binding("RemoteHost")
        {
            Mode = BindingMode.TwoWay,
            UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
        });
        Grid.SetRow(hostBox, 0);
        Grid.SetColumn(hostBox, 1);
        formGrid.Children.Add(hostBox);

        var hostHelp = new TextBlock
        {
            Text = "IP o DNS",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0),
            Foreground = (Brush)FindResource("TextDimBrush")
        };
        Grid.SetRow(hostHelp, 0);
        Grid.SetColumn(hostHelp, 2);
        formGrid.Children.Add(hostHelp);

        AddRemoteLabel(formGrid, "PUERTO DE SERVICIO", 1);
        var portBox = new TextBox
        {
            Height = 32,
            FontFamily = new FontFamily("Consolas"),
            ToolTip = "Puerto TCP del servicio remoto. Ejemplo: 3443."
        };
        portBox.SetBinding(TextBox.TextProperty, new Binding("RemotePort")
        {
            Mode = BindingMode.TwoWay,
            UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
        });
        Grid.SetRow(portBox, 1);
        Grid.SetColumn(portBox, 1);
        formGrid.Children.Add(portBox);

        var portHelp = new TextBlock
        {
            Text = "1–65535 · 3443 es un ejemplo",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0),
            Foreground = (Brush)FindResource("TextDimBrush")
        };
        Grid.SetRow(portHelp, 1);
        Grid.SetColumn(portHelp, 2);
        formGrid.Children.Add(portHelp);

        var testButton = new Button
        {
            Content = "PROBAR ENLACE",
            Width = 150,
            Height = 36,
            Margin = new Thickness(0, 12, 8, 0),
            Style = (Style)FindResource("SecondaryButton")
        };
        testButton.SetBinding(Button.CommandProperty, new Binding("TestRemoteConnectionCommand"));
        Grid.SetRow(testButton, 2);
        Grid.SetColumn(testButton, 1);
        formGrid.Children.Add(testButton);

        var searchButton = new Button
        {
            Content = "BUSCAR CÁMARAS",
            Width = 170,
            Height = 36,
            Margin = new Thickness(158, 12, 0, 0),
            Style = (Style)FindResource("PrimaryButton"),
            ToolTip = "Prueba el enlace y consulta el endpoint con los detectores compatibles."
        };
        searchButton.SetBinding(Button.CommandProperty, new Binding("SearchRemoteCamerasCommand"));
        Grid.SetRow(searchButton, 2);
        Grid.SetColumn(searchButton, 1);
        formGrid.Children.Add(searchButton);

        var status = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 12, 0, 0),
            Foreground = (Brush)FindResource("TextBrush")
        };
        status.SetBinding(TextBlock.TextProperty, new Binding("RemoteStatus"));
        Grid.SetRow(status, 3);
        Grid.SetColumn(status, 0);
        Grid.SetColumnSpan(status, 3);
        formGrid.Children.Add(status);

        form.Child = formGrid;
        panel.Children.Add(form);

        panel.Children.Add(new TextBlock
        {
            Text = "NOTA: host + puerto no implican por sí solos un proxy. Cuando el servicio remoto exponga un protocolo de enumeración compatible, sus cámaras se incorporarán a la misma tabla de resultados; no se marcará como cámara un servidor que solamente tenga un puerto abierto.",
            Margin = new Thickness(0, 14, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)FindResource("WarnBrush")
        });

        return new TabItem
        {
            Header = "CONEXIÓN DE ENLACE",
            Content = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = panel
            }
        };
    }

    private void AddRemoteLabel(Grid grid, string text, int row)
    {
        var label = new TextBlock
        {
            Text = text,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)FindResource("TextDimBrush"),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetRow(label, row);
        Grid.SetColumn(label, 0);
        grid.Children.Add(label);
    }
}
