using System.Windows;
using System.Windows.Controls;
using CameraInspector.App.ViewModels;

namespace CameraInspector.App;

public partial class MainWindow
{
    // Se conecta desde el Loaded principal de MainWindow para respetar el orden en que se crea el ContextMenu.
    private void ConfigureExtraContextMenu()
    {
        var dataGrid = FindVisualChild<DataGrid>(this);
        if (dataGrid is null || dataGrid.ContextMenu is null)
            return;

        var menu = dataGrid.ContextMenu;
        if (menu.Items.OfType<MenuItem>().Any(item => string.Equals(item.Header?.ToString(), "Ficha técnica", StringComparison.Ordinal)))
            return;

        var scanProfilesItem = new MenuItem { Header = "Perfiles de escaneo" };
        scanProfilesItem.Click += (_, _) => OpenScanProfilesWindow();

        var networkDiagnosticsItem = new MenuItem { Header = "Diagnóstico de red del PC" };
        networkDiagnosticsItem.Click += (_, _) => OpenNetworkDiagnosticsWindow();

        var technicalSheetItem = new MenuItem { Header = "Ficha técnica" };
        technicalSheetItem.Click += (_, _) => OpenTechnicalSheetWindow();

        menu.Items.Insert(0, scanProfilesItem);
        menu.Items.Insert(1, networkDiagnosticsItem);
        menu.Items.Insert(2, technicalSheetItem);
        menu.Items.Insert(3, new Separator());

        menu.Opened += (_, _) =>
        {
            if (DataContext is not MainViewModel viewModel)
            {
                scanProfilesItem.Visibility = Visibility.Collapsed;
                networkDiagnosticsItem.Visibility = Visibility.Collapsed;
                technicalSheetItem.Visibility = Visibility.Collapsed;
                return;
            }

            // No mostramos acciones que no tengan sentido en el estado actual.
            // Esto evita el menú lleno de opciones grises que después no se pueden ejecutar.
            scanProfilesItem.Visibility = Visibility.Visible;
            networkDiagnosticsItem.Visibility = viewModel.SelectedInterface is not null
                ? Visibility.Visible
                : Visibility.Collapsed;
            technicalSheetItem.Visibility = viewModel.SelectedDevice is not null
                ? Visibility.Visible
                : Visibility.Collapsed;

            // El separador solo aparece si hay más de una herramienta visible.
            if (menu.Items[3] is Separator separator)
            {
                var visibleTools = new[]
                {
                    scanProfilesItem,
                    networkDiagnosticsItem,
                    technicalSheetItem
                }.Count(item => item.Visibility == Visibility.Visible);

                separator.Visibility = visibleTools > 0 ? Visibility.Visible : Visibility.Collapsed;
            }
        };
    }

    private void OpenScanProfilesWindow()
    {
        if (DataContext is not MainViewModel viewModel)
            return;

        new ScanProfilesWindow(viewModel) { Owner = this }.ShowDialog();
    }

    private void OpenNetworkDiagnosticsWindow()
    {
        if (DataContext is not MainViewModel viewModel || viewModel.SelectedInterface is null)
            return;

        new NetworkDiagnosticsWindow(viewModel.SelectedInterface) { Owner = this }.ShowDialog();
    }

    private void OpenTechnicalSheetWindow()
    {
        if (DataContext is not MainViewModel viewModel || viewModel.SelectedDevice is null)
            return;

        new CameraTechnicalSheetWindow(viewModel.SelectedDevice) { Owner = this }.ShowDialog();
    }
}
