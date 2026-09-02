using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using CameraInspector.App.ViewModels;
using CameraInspector.Core.Models;

namespace CameraInspector.App;

/// <summary>
/// Mejora la experiencia del diagnóstico y retira temporalmente la gestión de credenciales
/// de la ventana principal. Las credenciales siguen disponibles para las operaciones autenticadas.
/// </summary>
public partial class MainWindow
{
    private static readonly bool _diagnosticsUiHook = RegisterDiagnosticsUiHook();
    private bool _diagnosticsUiReady;

    private static bool RegisterDiagnosticsUiHook()
    {
        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnDiagnosticsUiLoaded));
        return true;
    }

    private static void OnDiagnosticsUiLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not MainWindow window)
            return;

        window.ConfigureDiagnosticsPanel();
        window.HideCredentialsFromMainWindow();
    }

    private void HideCredentialsFromMainWindow()
    {
        var tab = FindTabByHeader(this, "CREDENCIALES");
        if (tab is not null)
            tab.Visibility = Visibility.Collapsed;

        foreach (var button in FindVisualChildren<Button>(this))
        {
            var text = button.Content?.ToString() ?? string.Empty;
            if (text.Contains("GUARDAR CREDENCIALES", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("ELIMINAR CREDENCIALES", StringComparison.OrdinalIgnoreCase))
                button.Visibility = Visibility.Collapsed;
        }
    }

    private void ConfigureDiagnosticsPanel()
    {
        if (_diagnosticsUiReady)
            return;

        var tab = FindTabByHeader(this, "DIAGNÓSTICO");
        if (tab?.Content is not Grid grid)
            return;

        var resultGrid = grid.Children.OfType<DataGrid>().FirstOrDefault();
        if (resultGrid is null)
            return;

        _diagnosticsUiReady = true;
        resultGrid.AutoGenerateColumns = false;
        resultGrid.Columns.Clear();
        resultGrid.GridLinesVisibility = DataGridGridLinesVisibility.Horizontal;
        resultGrid.AlternatingRowBackground = (Brush)Application.Current.FindResource("Panel2Brush");
        resultGrid.Columns.Add(new DataGridTextColumn { Header = "PRUEBA", Binding = new Binding(nameof(DiagnosticResult.TestName)), Width = 145 });
        resultGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "RESULTADO",
            Binding = new Binding(nameof(DiagnosticResult.Success)) { Converter = new ResultTextConverter() },
            Width = 105
        });
        resultGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "TIEMPO",
            Binding = new Binding(nameof(DiagnosticResult.Duration)) { StringFormat = "{0:mm\\:ss\\.fff}" },
            Width = 105
        });
        resultGrid.Columns.Add(new DataGridTextColumn { Header = "DETALLE", Binding = new Binding(nameof(DiagnosticResult.Message)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });

        resultGrid.LoadingRow += (_, args) =>
        {
            if (args.Row.DataContext is not DiagnosticResult result)
                return;
            args.Row.Foreground = result.NotSupported
                ? (Brush)Application.Current.FindResource("TextDimBrush")
                : result.Success
                    ? (Brush)Application.Current.FindResource("AccentBrush")
                    : (Brush)Application.Current.FindResource("ErrBrush");
        };

        var runButton = grid.Children.OfType<Button>().FirstOrDefault();
        if (runButton is not null)
        {
            runButton.Command = null;
            runButton.Content = "⚙ EJECUTAR BATERÍA";
            runButton.Width = 175;
            runButton.Click += async (_, _) =>
            {
                if (DataContext is MainViewModel vm)
                    await vm.RunQuickDiagnosticsCommand.ExecuteAsync(null);
            };
        }

        var controls = new StackPanel { Orientation = Orientation.Horizontal };
        if (runButton is not null)
        {
            grid.Children.Remove(runButton);
            controls.Children.Add(runButton);
        }

        var healthButton = new Button
        {
            Content = "↻ VERIFICAR SALUD",
            Width = 160,
            Height = 32,
            Margin = new Thickness(7, 0, 0, 0),
            Style = FindResource("PrimaryButton") as Style
        };
        healthButton.Click += async (_, _) =>
        {
            if (DataContext is not MainViewModel vm || vm.SelectedDevice is null)
            {
                SetStatus("ALERTA: seleccione una cámara antes de verificar la salud.");
                return;
            }
            try
            {
                SetStatus($"Verificando comunicación y vídeo de {vm.SelectedDevice.IpAddress}...");
                await vm.RecheckSelectedHealthAsync();
                SetStatus(vm.SelectedDevice.AlertDisplay);
            }
            catch (Exception ex)
            {
                SetStatus($"ALERTA: error verificando salud: {ex.Message}");
            }
        };
        controls.Children.Add(healthButton);

        var cancelButton = new Button { Content = "■ DETENER", Width = 115, Height = 32, Margin = new Thickness(7, 0, 0, 0) };
        cancelButton.Click += (_, _) =>
        {
            if (DataContext is MainViewModel vm)
                vm.CancelDiagnosticsCommand.Execute(null);
        };
        controls.Children.Add(cancelButton);
        Grid.SetRow(controls, 0);
        grid.Children.Add(controls);

        var footer = grid.Children.OfType<TextBlock>().FirstOrDefault();
        if (footer is not null)
        {
            footer.Text = "Diagnóstico sin credenciales: red, puertos, RTSP, ONVIF, Media y salud. Las funciones autenticadas informan ALERTA cuando requieren acceso.";
            footer.Foreground = (Brush)Application.Current.FindResource("TextDimBrush");
        }
    }

    private void SetStatus(string value)
    {
        if (DataContext is MainViewModel vm)
            vm.StatusText = value;
    }

    private static TabItem? FindTabByHeader(DependencyObject root, string header)
    {
        foreach (var tab in FindVisualChildren<TabItem>(root))
            if (string.Equals(tab.Header?.ToString(), header, StringComparison.OrdinalIgnoreCase))
                return tab;
        return null;
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root) where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T typed)
                yield return typed;
            foreach (var nested in FindVisualChildren<T>(child))
                yield return nested;
        }
    }

    private sealed class ResultTextConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            => value is bool ok && ok ? "OK" : "ALERTA";

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            => Binding.DoNothing;
    }
}
