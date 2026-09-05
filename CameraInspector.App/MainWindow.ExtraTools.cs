using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using CameraInspector.App.ViewModels;

namespace CameraInspector.App;

public partial class MainWindow
{
    // Se ejecuta antes del constructor y deja el enganche preparado sin tocar el layout XAML existente.
    private readonly bool _extraToolsHook = HookExtraTools();

    private bool HookExtraTools()
    {
        Loaded += (_, _) => Dispatcher.BeginInvoke(
            DispatcherPriority.Background,
            new Action(ConfigureExtraContextMenu));
        return true;
    }

    private void ConfigureExtraContextMenu()
    {
        var dataGrid = FindVisualChild<DataGrid>(this);
        if (dataGrid is null || dataGrid.ContextMenu is null)
            return;

        var menu = dataGrid.ContextMenu;
        if (menu.Items.OfType<MenuItem>().Any(item => string.Equals(item.Header?.ToString(), "Ficha técnica", StringComparison.Ordinal)))
            return;

        var networkDiagnosticsItem = new MenuItem { Header = "Diagnóstico de red del PC" };
        networkDiagnosticsItem.Click += (_, _) => OpenNetworkDiagnosticsWindow();

        var technicalSheetItem = new MenuItem { Header = "Ficha técnica" };
        technicalSheetItem.Click += (_, _) => OpenTechnicalSheetWindow();

        menu.Items.Insert(0, networkDiagnosticsItem);
        menu.Items.Insert(1, technicalSheetItem);
        menu.Items.Insert(2, new Separator());

        menu.Opened += (_, _) =>
        {
            var viewModel = DataContext as MainViewModel;
            networkDiagnosticsItem.IsEnabled = viewModel?.SelectedInterface is not null;
            technicalSheetItem.IsEnabled = viewModel?.SelectedDevice is not null;
        };
    }

    private void OpenNetworkDiagnosticsWindow()
    {
        if (DataContext is not MainViewModel viewModel || viewModel.SelectedInterface is null)
        {
            MessageBox.Show(
                "No hay una interfaz de red seleccionada.",
                "Camera Inspector — Diagnóstico de red",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        new NetworkDiagnosticsWindow(viewModel.SelectedInterface) { Owner = this }.ShowDialog();
    }

    private void OpenTechnicalSheetWindow()
    {
        if (DataContext is not MainViewModel viewModel || viewModel.SelectedDevice is null)
            return;

        new CameraTechnicalSheetWindow(viewModel.SelectedDevice) { Owner = this }.ShowDialog();
    }
}