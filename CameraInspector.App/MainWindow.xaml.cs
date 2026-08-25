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
    private readonly ICameraProviderResolver _providerResolver;
    private readonly IVivotekSnapshotService _vivotekSnapshotService;
    private readonly IVivotekPtzService _vivotekPtzService;
    private readonly ICredentialStore _credentialStore;
    private readonly ICameraCredentialStore _cameraCredentialStore;

    public MainWindow(
        MainViewModel viewModel,
        IVideoPlayerService videoPlayerService,
        IOnvifImagingService imagingService,
        IOnvifEventService eventService,
        ICameraProviderResolver providerResolver,
        IVivotekSnapshotService vivotekSnapshotService,
        IVivotekPtzService vivotekPtzService,
        ICredentialStore credentialStore,
        ICameraCredentialStore cameraCredentialStore)
    {
        InitializeComponent();

        // DataContext conecta todos los bindings de la ventana con MainViewModel.
        DataContext = viewModel;
        _videoPlayerService = videoPlayerService;
        _imagingService = imagingService;
        _eventService = eventService;
        _providerResolver = providerResolver;
        _vivotekSnapshotService = vivotekSnapshotService;
        // _vivotekPtzService ejecuta exclusivamente los comandos PTZ propietarios de VIVOTEK.
        _vivotekPtzService = vivotekPtzService;
        _credentialStore = credentialStore;
        _cameraCredentialStore = cameraCredentialStore;

        // VideoSurface recibe el MediaPlayer mantenido por el servicio singleton.
        VideoSurface.MediaPlayer = videoPlayerService.Player;

        // Agregamos acciones avanzadas mediante menú contextual para no saturar la UI principal.
        Loaded += (_, _) => ConfigureCameraContextMenu();
    }

    private void ConfigureCameraContextMenu()
    {
        var dataGrid = FindVisualChild<DataGrid>(this);
        if (dataGrid is null)
            return;

        var contextMenu = new ContextMenu();
        var ptzItem = new MenuItem { Header = "Control PTZ" };
        var imagingItem = new MenuItem { Header = "Ajustes de imagen" };
        var eventsItem = new MenuItem { Header = "Eventos ONVIF" };
        var providerItem = new MenuItem { Header = "Información propietaria" };
        var snapshotItem = new MenuItem { Header = "Capturar snapshot" };
        var vivotekSnapshotItem = new MenuItem { Header = "Snapshot VIVOTEK" };
        var vivotekPtzItem = new MenuItem { Header = "Control PTZ VIVOTEK" };

        ptzItem.Click += (_, _) => OpenPtzWindow();
        imagingItem.Click += (_, _) => OpenImagingWindow();
        eventsItem.Click += (_, _) => OpenEventsWindow();
        providerItem.Click += (_, _) => OpenProviderInfoWindow();
        snapshotItem.Click += (_, _) => SaveSnapshot();
        vivotekSnapshotItem.Click += async (_, _) => await SaveVivotekSnapshotAsync();
        vivotekPtzItem.Click += (_, _) => OpenVivotekPtzWindow();

        contextMenu.Items.Add(ptzItem);
        contextMenu.Items.Add(vivotekPtzItem);
        contextMenu.Items.Add(imagingItem);
        contextMenu.Items.Add(eventsItem);
        contextMenu.Items.Add(providerItem);
        contextMenu.Items.Add(new Separator());
        contextMenu.Items.Add(snapshotItem);
        contextMenu.Items.Add(vivotekSnapshotItem);
        dataGrid.ContextMenu = contextMenu;

        contextMenu.Opened += (_, _) =>
        {
            if (DataContext is not MainViewModel viewModel || viewModel.SelectedDevice is null)
            {
                ptzItem.IsEnabled = false;
                vivotekPtzItem.IsEnabled = false;
                imagingItem.IsEnabled = false;
                eventsItem.IsEnabled = false;
                providerItem.IsEnabled = false;
                snapshotItem.IsEnabled = false;
                vivotekSnapshotItem.IsEnabled = false;
                return;
            }

            var selected = viewModel.SelectedDevice;
            var isVivotek = IsVivotekDevice(selected.Device);

            ptzItem.IsEnabled = selected.HasPtzService;
            vivotekPtzItem.IsEnabled = isVivotek;
            imagingItem.IsEnabled = selected.HasImagingService;
            eventsItem.IsEnabled = selected.HasEventsService;
            providerItem.IsEnabled = _providerResolver.Resolve(selected.Device) is not null;
            snapshotItem.IsEnabled = _videoPlayerService.Player.IsPlaying;
            vivotekSnapshotItem.IsEnabled = isVivotek;
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

        new PtzWindow(viewModel) { Owner = this }.ShowDialog();
    }

    private void OpenVivotekPtzWindow()
    {
        if (DataContext is not MainViewModel viewModel || viewModel.SelectedDevice is null)
            return;

        if (!IsVivotekDevice(viewModel.SelectedDevice.Device))
        {
            ShowInformation("La cámara seleccionada no fue identificada como VIVOTEK.", "PTZ VIVOTEK");
            return;
        }

        new VivotekPtzWindow(
            viewModel.SelectedDevice,
            _vivotekPtzService,
            _credentialStore,
            _cameraCredentialStore)
        {
            Owner = this
        }.ShowDialog();
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

        new ImagingWindow(viewModel.SelectedDevice, _imagingService, _credentialStore, _cameraCredentialStore)
        {
            Owner = this
        }.ShowDialog();
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

        new EventsWindow(viewModel.SelectedDevice, _eventService, _credentialStore, _cameraCredentialStore)
        {
            Owner = this
        }.ShowDialog();
    }

    private void OpenProviderInfoWindow()
    {
        if (DataContext is not MainViewModel viewModel || viewModel.SelectedDevice is null)
            return;

        if (_providerResolver.Resolve(viewModel.SelectedDevice.Device) is null)
        {
            ShowInformation("No existe un provider propietario compatible con esta cámara.", "Provider");
            return;
        }

        new ProviderInfoWindow(
            viewModel.SelectedDevice,
            _providerResolver,
            _credentialStore,
            _cameraCredentialStore)
        {
            Owner = this
        }.ShowDialog();
    }

    private void SaveSnapshot()
    {
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

    private async Task SaveVivotekSnapshotAsync()
    {
        if (DataContext is not MainViewModel viewModel || viewModel.SelectedDevice is null)
            return;

        var selected = viewModel.SelectedDevice;
        if (!IsVivotekDevice(selected.Device))
        {
            ShowInformation("La cámara seleccionada no fue identificada como VIVOTEK.", "Snapshot VIVOTEK");
            return;
        }

        if (selected.CameraId is not int cameraId)
        {
            ShowInformation("La cámara todavía no tiene identidad persistente en el inventario.", "Snapshot VIVOTEK");
            return;
        }

        var savedInfo = await _cameraCredentialStore.GetAsync(cameraId);
        if (savedInfo is null)
        {
            ShowInformation("No hay credenciales guardadas para esta cámara. Guárdelas primero desde el panel principal.", "Snapshot VIVOTEK");
            return;
        }

        var credentials = await _credentialStore.GetAsync(savedInfo.CredentialRef);
        if (credentials is null)
        {
            ShowInformation("La credencial asociada ya no existe en Windows Credential Manager.", "Snapshot VIVOTEK");
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Guardar snapshot VIVOTEK",
            Filter = "Imagen JPEG (*.jpg)|*.jpg",
            DefaultExt = ".jpg",
            AddExtension = true,
            FileName = $"vivotek_{DateTime.Now:yyyyMMdd_HHmmss}.jpg"
        };

        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            var saved = await _vivotekSnapshotService.SaveSnapshotAsync(
                selected.IpAddress,
                credentials.Username,
                credentials.Password,
                dialog.FileName);

            MessageBox.Show(
                saved
                    ? $"Snapshot VIVOTEK guardado correctamente.\n\n{dialog.FileName}"
                    : "La cámara no devolvió un snapshot válido.",
                "Camera Inspector — Snapshot VIVOTEK",
                MessageBoxButton.OK,
                saved ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"No se pudo obtener el snapshot VIVOTEK:\n\n{ex.Message}",
                "Camera Inspector — Snapshot VIVOTEK",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private static bool IsVivotekDevice(CameraInspector.Core.Models.DiscoveredDevice device)
    {
        var manufacturer = device.Manufacturer ?? string.Empty;
        return manufacturer.Contains("VIVOTEK", StringComparison.OrdinalIgnoreCase);
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
