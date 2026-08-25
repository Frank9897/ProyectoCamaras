using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using CameraInspector.App.ViewModels;

namespace CameraInspector.App;

public partial class MainWindow
{
    /// <summary>Elemento dinámico utilizado para iniciar la grabación desde el menú contextual.</summary>
    private MenuItem? _startRecordingItem;

    /// <summary>Elemento dinámico utilizado para detener la grabación actual.</summary>
    private MenuItem? _stopRecordingItem;

    /// <summary>
    /// Se ejecuta después de la inicialización del Window y agrega handlers complementarios
    /// sin modificar el constructor principal de la ventana.
    /// </summary>
    protected override void OnInitialized(EventArgs e)
    {
        base.OnInitialized(e);

        // Loaded se ejecuta después de que el menú contextual principal haya sido creado.
        Loaded += ConfigureRecordingContextMenu;
        // Closed garantiza que una grabación activa se cierre correctamente al salir de la aplicación.
        Closed += (_, _) =>
        {
            if (DataContext is MainViewModel viewModel)
                viewModel.StopRecording();
        };
    }

    /// <summary>
    /// Agrega las acciones de grabación al menú contextual que ya utiliza MainWindow.
    /// </summary>
    private void ConfigureRecordingContextMenu(object? sender, RoutedEventArgs e)
    {
        var dataGrid = FindVisualChild<DataGrid>(this);
        if (dataGrid?.ContextMenu is null)
            return;

        // Evitamos agregar los mismos elementos dos veces si WPF vuelve a disparar Loaded.
        if (_startRecordingItem is not null)
            return;

        _startRecordingItem = new MenuItem { Header = "Iniciar grabación RTSP" };
        _stopRecordingItem = new MenuItem { Header = "Detener grabación RTSP" };

        _startRecordingItem.Click += async (_, _) => await StartRecordingFromMenuAsync();
        _stopRecordingItem.Click += (_, _) => StopRecordingFromMenu();

        // Separamos la grabación de las acciones de snapshot y video para conservar el menú legible.
        dataGrid.ContextMenu.Items.Add(new Separator());
        dataGrid.ContextMenu.Items.Add(_startRecordingItem);
        dataGrid.ContextMenu.Items.Add(_stopRecordingItem);

        dataGrid.ContextMenu.Opened += (_, _) => RefreshRecordingMenuState();
    }

    private void RefreshRecordingMenuState()
    {
        if (_startRecordingItem is null || _stopRecordingItem is null)
            return;

        if (DataContext is not MainViewModel viewModel || viewModel.SelectedDevice is null)
        {
            _startRecordingItem.IsEnabled = false;
            _stopRecordingItem.IsEnabled = false;
            return;
        }

        // Solo habilitamos grabación cuando el Main Stream ya fue resuelto y no existe otra grabación.
        _startRecordingItem.IsEnabled = viewModel.ResolvedMainStream is not null && !viewModel.IsRecording;
        // Detener solo tiene sentido cuando el ViewModel mantiene una grabación activa.
        _stopRecordingItem.IsEnabled = viewModel.IsRecording;
    }

    private async Task StartRecordingFromMenuAsync()
    {
        if (DataContext is not MainViewModel viewModel || viewModel.SelectedDevice is null)
            return;

        if (viewModel.ResolvedMainStream is null)
        {
            ShowInformation("Primero resuelva el Main Stream de la cámara y luego inicie la grabación.", "Grabación RTSP");
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Guardar grabación RTSP",
            Filter = "MPEG-TS (*.ts)|*.ts",
            DefaultExt = ".ts",
            AddExtension = true,
            FileName = $"grabacion_{viewModel.SelectedDevice.IpAddress}_{DateTime.Now:yyyyMMdd_HHmmss}.ts"
        };

        if (dialog.ShowDialog(this) != true)
            return;

        // started reutiliza la misma cadena segura de credenciales que ONVIF y RTSP.
        var started = await viewModel.StartRecordingAsync(dialog.FileName);

        if (!started)
        {
            ShowInformation(viewModel.StatusText, "Grabación RTSP");
            return;
        }

        RefreshRecordingMenuState();
    }

    private void StopRecordingFromMenu()
    {
        if (DataContext is not MainViewModel viewModel)
            return;

        // StopRecording finaliza la salida sout y libera el reproductor secundario.
        viewModel.StopRecording();
        RefreshRecordingMenuState();
    }
}
