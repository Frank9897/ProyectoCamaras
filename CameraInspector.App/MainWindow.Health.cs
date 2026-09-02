using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using CameraInspector.App.ViewModels;

namespace CameraInspector.App;

/// <summary>
/// Complementa el listado principal sin alterar el XAML histórico.
/// Agrega salud, comunicación, vídeo y alerta directamente en cada fila y mantiene las acciones accesibles
/// aunque la cámara esté fuera de servicio.
/// </summary>
public partial class MainWindow
{
    private bool _healthUiConfigured;

    static MainWindow()
    {
        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnMainWindowLoadedForHealth));
    }

    private static void OnMainWindowLoadedForHealth(object sender, RoutedEventArgs e)
    {
        if (sender is MainWindow window)
            window.ConfigureHealthUi();
    }

    private void ConfigureHealthUi()
    {
        if (_healthUiConfigured)
            return;

        var dataGrid = FindVisualChild<DataGrid>(this);
        if (dataGrid is null)
            return;

        _healthUiConfigured = true;

        AddHealthColumn(dataGrid, "SALUD", nameof(DeviceViewModel.HealthDisplay), 125);
        AddHealthColumn(dataGrid, "COMUNICACIÓN", nameof(DeviceViewModel.CommunicationDisplay), 115);
        AddHealthColumn(dataGrid, "VÍDEO", nameof(DeviceViewModel.VideoDisplay), 95);
        AddHealthColumn(dataGrid, "ALERTA", nameof(DeviceViewModel.AlertDisplay), 205);

        ConfigureMainHealthContextMenu(dataGrid);
    }

    private static void AddHealthColumn(DataGrid dataGrid, string header, string property, double width)
    {
        var column = new DataGridTemplateColumn
        {
            Header = header,
            Width = width,
            SortMemberPath = property
        };

        var template = new DataTemplate(typeof(DeviceViewModel));
        var text = new FrameworkElementFactory(typeof(TextBlock));
        text.SetValue(TextBlock.FontFamilyProperty, new FontFamily("Consolas"));
        text.SetValue(TextBlock.FontSizeProperty, 10d);
        text.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
        text.SetValue(TextBlock.TextWrappingProperty, TextWrapping.NoWrap);
        text.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        text.SetBinding(TextBlock.TextProperty, new Binding(property) { Mode = BindingMode.OneWay });

        var foreground = new Binding(nameof(DeviceViewModel.HealthState))
        {
            Mode = BindingMode.OneWay,
            Converter = new HealthStateBrushConverter()
        };
        text.SetBinding(TextBlock.ForegroundProperty, foreground);

        template.VisualTree = text;
        column.CellTemplate = template;
        dataGrid.Columns.Add(column);
    }

    private void ConfigureMainHealthContextMenu(DataGrid dataGrid)
    {
        if (dataGrid.ContextMenu is null)
            return;

        var healthItem = new MenuItem { Header = "↻ Comprobar salud" };
        var videoItem = new MenuItem { Header = "▶ Abrir reproductor IP" };
        var webItem = new MenuItem { Header = "▣ Abrir interfaz web" };

        healthItem.Click += async (_, _) =>
        {
            if (DataContext is MainViewModel vm)
            {
                try
                {
                    await vm.RecheckSelectedHealthAsync();
                    ConfigureHealthUi();
                }
                catch (Exception ex)
                {
                    vm.StatusText = $"No se pudo comprobar la salud: {ex.Message}";
                }
            }
        };

        videoItem.Click += (_, _) => OpenIpCameraVideoWindow();
        webItem.Click += (_, _) => OpenSelectedCameraWeb();

        dataGrid.ContextMenu.Items.Insert(0, healthItem);
        dataGrid.ContextMenu.Items.Insert(1, videoItem);
        dataGrid.ContextMenu.Items.Insert(2, webItem);
        dataGrid.ContextMenu.Items.Insert(3, new Separator());

        dataGrid.ContextMenu.Opened += (_, _) =>
        {
            var hasSelection = DataContext is MainViewModel vm && vm.SelectedDevice is not null;
            healthItem.IsEnabled = hasSelection;
            videoItem.IsEnabled = hasSelection;
            webItem.IsEnabled = hasSelection;
        };
    }

    private void OpenSelectedCameraWeb()
    {
        if (DataContext is not MainViewModel vm || vm.SelectedDevice is null)
            return;

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = $"http://{vm.SelectedDevice.IpAddress}",
                UseShellExecute = true
            });
            vm.StatusText = $"Abriendo interfaz web de {vm.SelectedDevice.IpAddress}...";
        }
        catch (Exception ex)
        {
            vm.StatusText = $"No se pudo abrir la interfaz web: {ex.Message}";
        }
    }

    private sealed class HealthStateBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            var state = value is Core.Models.CameraHealthState health ? health : Core.Models.CameraHealthState.Unknown;
            return state switch
            {
                Core.Models.CameraHealthState.Healthy => (Brush)Application.Current.FindResource("AccentBrush"),
                Core.Models.CameraHealthState.AuthenticationRequired => (Brush)Application.Current.FindResource("WarnBrush"),
                Core.Models.CameraHealthState.NoVideo => (Brush)Application.Current.FindResource("WarnBrush"),
                Core.Models.CameraHealthState.CommunicationOnly => (Brush)Application.Current.FindResource("WarnBrush"),
                Core.Models.CameraHealthState.NoResponse => (Brush)Application.Current.FindResource("ErrBrush"),
                Core.Models.CameraHealthState.Degraded => (Brush)Application.Current.FindResource("WarnBrush"),
                _ => (Brush)Application.Current.FindResource("TextDimBrush")
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => Binding.DoNothing;
    }
}
