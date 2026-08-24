using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CameraInspector.App.ViewModels;
using CameraInspector.Core.Interfaces;
using CameraInspector.Video;

namespace CameraInspector.App;

/// <summary>
/// Code-behind mínimo de la ventana.
/// Su responsabilidad visual adicional es conectar LibVLC y ofrecer acciones contextuales
/// que no justifican agregar más botones permanentes al layout principal.
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow(
        MainViewModel viewModel,
        IVideoPlayerService videoPlayerService)
    {
        InitializeComponent();

        // DataContext conecta todos los bindings de la ventana con MainViewModel.
        DataContext = viewModel;

        // VideoSurface recibe el MediaPlayer mantenido por el servicio singleton.
        // El ViewModel controla cuándo reproducir o detener; la View solamente renderiza.
        VideoSurface.MediaPlayer = videoPlayerService.Player;

        // Agregamos acciones avanzadas mediante menú contextual para no saturar la UI principal.
        Loaded += (_, _) => ConfigureCameraContextMenu();
    }

    private void ConfigureCameraContextMenu()
    {
        // dataGrid es la tabla principal donde el técnico selecciona una cámara.
        var dataGrid = FindVisualChild<DataGrid>(this);
        if (dataGrid is null)
            return;

        var contextMenu = new ContextMenu();
        var ptzItem = new MenuItem
        {
            Header = "Control PTZ"
        };

        ptzItem.Click += (_, _) => OpenPtzWindow();
        contextMenu.Items.Add(ptzItem);
        dataGrid.ContextMenu = contextMenu;

        // El menú solo puede usarse cuando existe una cámara seleccionada que realmente expone PTZ.
        contextMenu.Opened += (_, _) =>
        {
            if (DataContext is MainViewModel viewModel && viewModel.SelectedDevice is not null)
                ptzItem.IsEnabled = viewModel.SelectedDevice.HasPtzService;
            else
                ptzItem.IsEnabled = false;
        };
    }

    private void OpenPtzWindow()
    {
        if (DataContext is not MainViewModel viewModel)
            return;

        if (viewModel.SelectedDevice?.HasPtzService != true)
        {
            MessageBox.Show(
                "La cámara seleccionada no anuncia un servicio PTZ ONVIF.",
                "Camera Inspector — PTZ",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var window = new PtzWindow(viewModel)
        {
            Owner = this
        };

        window.ShowDialog();
    }

    /// <summary>
    /// Busca recursivamente un control visual de tipo T dentro de la ventana.
    /// </summary>
    private static T? FindVisualChild<T>(DependencyObject parent)
        where T : DependencyObject
    {
        var childrenCount = VisualTreeHelper.GetChildrenCount(parent);

        for (var index = 0; index < childrenCount; index++)
        {
            // child representa el elemento visual actual que estamos recorriendo.
            var child = VisualTreeHelper.GetChild(parent, index);

            if (child is T typedChild)
                return typedChild;

            var nestedResult = FindVisualChild<T>(child);
            if (nestedResult is not null)
                return nestedResult;
        }

        return null;
    }
}
