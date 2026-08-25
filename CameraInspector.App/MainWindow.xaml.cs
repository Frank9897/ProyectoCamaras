using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using CameraInspector.App.ViewModels;
using CameraInspector.Core.Interfaces;
using CameraInspector.Video;

namespace CameraInspector.App;

/// <summary>
/// Code-behind mínimo de la ventana.
/// Su responsabilidad visual adicional es conectar LibVLC y ofrecer acciones contextuales
/// para capacidades avanzadas sin saturar el layout principal.
/// </summary>
public partial class MainWindow : Window
{
    private readonly IVideoPlayerService _videoPlayerService;
    private readonly IOnvifImagingService _imagingService;
    private readonly IOnvifEventService _eventService;
    private readonly ICredentialStore _credentialStore;
    private readonly ICameraCredentialStore _cameraCredentialStore;

    public MainWindow(
        MainViewModel viewModel,
        IVideoPlayerService videoPlayerService,
        IOnvifImagingService imagingService,
        IOnvifEventService eventService,
        ICredentialStore credentialStore,
        ICameraCredentialStore cameraCredentialStore)
    {
        InitializeComponent();

        // DataContext conecta todos los bindings de la ventana con MainViewModel.
        DataContext = viewModel;
        // _videoPlayerService administra el motor multimedia y conserva el MediaPlayer durante la vida de la ventana.
        _videoPlayerService = videoPlayerService;
        // Los siguientes servicios ejecutan las capacidades ONVIF avanzadas de la cámara seleccionada.
        _imagingService = imagingService;
        _eventService = eventService;
        _credentialStore = credentialStore;
        _cameraCredentialStore = cameraCredentialStore;

        // VideoSurface recibe el MediaPlayer mantenido por el servicio singleton.
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
        var ptzItem = new MenuItem { Header = "Control PTZ" };
        var imagingItem = new MenuItem { Header = "Ajustes de imagen" };
        var eventsItem = new MenuItem { Header = "Eventos ONVIF" };
        var snapshotItem = new MenuItem { Header = "Capturar snapshot" };

        ptzItem.Click += (_, _) => OpenPtzWindow();
        imagingItem.Click += (_, _) => OpenImagingWindow();
        eventsItem.Click += (_, _) => OpenEventsWindow();
        snapshotItem.Click += (_, _) => SaveSnapshot();

        contextMenu.Items.Add(ptzItem);
        contextMenu.Items.Add(imagingItem);
        contextMenu.Items.Add(eventsItem);
        contextMenu.Items.Add(new Separator());
        contextMenu.Items.Add(snapshotItem);
        dataGrid.ContextMenu = contextMenu;

        // El menú se recalcula cada vez que se abre para reflejar las capacidades reales de la cámara seleccionada.
        contextMenu.Opened += (_, _) =>
        {
            if (DataContext is not MainViewModel viewModel || viewModel.SelectedDevice is null)
            {
                ptzItem.IsEnabled = false;
                imagingItem.IsEnabled = false;
                eventsItem.IsEnabled = false;
                snapshotItem.IsEnabled = false;
                return;
            }

            var selected = viewModel.SelectedDevice;
            ptzItem.IsEnabled = selected.HasPtzService;
            imagingItem.IsEnabled = selected.HasImagingService;
            eventsItem.IsEnabled = selected.HasEventsService;
            snapshotItem.IsEnabled = _videoPlayerService.Player.IsPlaying;
        };
    }

    private void OpenPtzWindow()
    {
        if (DataContext is not MainViewModel viewModel || viewModel.SelectedDevice is null)
            return;

        if (!viewModel.SelectedDevice.HasPtzService)
        {
            ShowInformation("La cámara seleccionada no anuncia un servicio PTZ ONVIF.", "PTZ");
            return;
        }

        var window = new PtzWindow(viewModel) { Owner = this };
        window.ShowDialog();
    }

    private void OpenImagingWindow()
    {
        if (DataContext is not MainViewModel viewModel || viewModel.SelectedDevice is null)
            return;

        if (!viewModel.SelectedDevice.HasImagingService)
        {
            ShowInformation("La cámara seleccionada no anuncia un servicio Imaging ONVIF.", "Imaging");
            return;
        }

        var window = new ImagingWindow(
            viewModel.SelectedDevice,
            _imagingService,
            _credentialStore,
            _cameraCredentialStore)
        { Owner = this };

        window.ShowDialog();
    }

    private void OpenEventsWindow()
    {
        if (DataContext is not MainViewModel viewModel || viewModel.SelectedDevice is null)
            return;

        if (!viewModel.SelectedDevice.HasEventsService)
        {
            ShowInformation("La cámara seleccionada no anuncia un servicio de eventos ONVIF.", "Eventos");
            return;
        }

        var window = new EventsWindow(
            viewModel.SelectedDevice,
            _eventService,
            _credentialStore,
            _cameraCredentialStore)
        { Owner = this };

        window.ShowDialog();
    }

    private void SaveSnapshot()
    {
        // snapshot solo puede ejecutarse cuando LibVLC ya tiene una salida de video activa.
        if (!_videoPlayerService.Player.IsPlaying)
        {
            ShowInformation("No existe una reproducción de video activa para capturar.", "Snapshot");
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Guardar snapshot",
            Filter = "Imagen PNG (*.png)|*.png",
            DefaultExt = ".png",
            AddExtension = true,
            FileName = $"camera_{DateTime.Now:yyyyMMdd_HHmmss}.png"
        };

        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            // saved indica si LibVLC aceptó la solicitud de captura del frame actual.
            var saved = _videoPlayerService.TakeSnapshot(dialog.FileName);

            if (!saved)
            {
                ShowInformation("LibVLC todavía no tiene un frame disponible. Intente nuevamente en unos segundos.", "Snapshot");
                return;
            }

            MessageBox.Show(
                $"Snapshot solicitado correctamente.\n\n{dialog.FileName}",
                "Camera Inspector — Snapshot",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"No se pudo capturar el snapshot:\n\n{ex.Message}",
                "Camera Inspector — Snapshot",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private static void ShowInformation(string message, string title) =>
        MessageBox.Show(message, $"Camera Inspector — {title}", MessageBoxButton.OK, MessageBoxImage.Information);

    /// <summary>Busca recursivamente un control visual de tipo T dentro de la ventana.</summary>
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
