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

            ApplyExistingContextMenuVisibility(menu, viewModel);

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

    private void ApplyExistingContextMenuVisibility(ContextMenu menu, MainViewModel viewModel)
    {
        var selected = viewModel.SelectedDevice;
        var hasDevice = selected is not null;
        var isVivotek = hasDevice && IsVivotekDevice(selected!.Device);

        SetContextMenuVisibility(menu, "Exportar inventario CSV", viewModel.Devices.Count > 0);
        SetContextMenuVisibility(menu, "Exportar historial CSV", hasDevice && viewModel.DiagnosticHistory.Count > 0);
        SetContextMenuVisibility(menu, "Configuración de red", hasDevice && selected!.OnvifSupported);
        SetContextMenuVisibility(menu, "Control PTZ", hasDevice && selected!.HasPtzService);
        SetContextMenuVisibility(menu, "Control PTZ VIVOTEK", isVivotek);
        SetContextMenuVisibility(menu, "Parámetros VIVOTEK", isVivotek);
        SetContextMenuVisibility(menu, "Ajustes de imagen", hasDevice && selected!.HasImagingService);
        SetContextMenuVisibility(menu, "Eventos ONVIF", hasDevice && selected!.HasEventsService);
        SetContextMenuVisibility(menu, "Información propietaria", hasDevice && _providerResolver.Resolve(selected!.Device) is not null);
        SetContextMenuVisibility(menu, "Capturar snapshot", _videoPlayerService.Player.IsPlaying);
        SetContextMenuVisibility(menu, "Snapshot VIVOTEK", isVivotek);

        // Los separadores que ya existían en el menú también desaparecen si dejan un bloque vacío.
        var separators = menu.Items.OfType<Separator>().ToList();
        foreach (var separator in separators)
        {
            var index = menu.Items.IndexOf(separator);
            var previousVisible = FindVisibleMenuItem(menu, index, -1);
            var nextVisible = FindVisibleMenuItem(menu, index, 1);
            separator.Visibility = previousVisible is not null && nextVisible is not null
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
    }

    private static void SetContextMenuVisibility(ContextMenu menu, string header, bool visible)
    {
        foreach (var item in menu.Items.OfType<MenuItem>())
        {
            if (string.Equals(item.Header?.ToString(), header, StringComparison.Ordinal))
                item.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private static MenuItem? FindVisibleMenuItem(ContextMenu menu, int startIndex, int direction)
    {
        for (var index = startIndex + direction; index >= 0 && index < menu.Items.Count; index += direction)
        {
            if (menu.Items[index] is MenuItem item && item.Visibility == Visibility.Visible)
                return item;

            if (menu.Items[index] is Separator)
                continue;
        }

        return null;
    }

    private void OpenScanProfilesWindow()
    {
        if (DataContext is not MainViewModel viewModel)
            return;

        new ScanProfilesWindow(viewModel) { Owner = this }.ShowDialog();
    }

    private void OpenScanProfilesFromMain_Click(object sender, RoutedEventArgs e)
    {
        OpenScanProfilesWindow();
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
