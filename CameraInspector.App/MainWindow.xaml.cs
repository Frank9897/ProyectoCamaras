using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using CameraInspector.App.ViewModels;
using CameraInspector.Core.Interfaces;
using CameraInspector.Core.Services;
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
    private readonly IOnvifDeviceService _onvifDeviceService;
    private readonly IOnvifImagingService _imagingService;
    private readonly IOnvifEventService _eventService;
    private readonly ICameraProviderResolver _providerResolver;
    private readonly IVivotekSnapshotService _vivotekSnapshotService;
    private readonly IVivotekPtzService _vivotekPtzService;
    private readonly IVivotekParameterService _vivotekParameterService;
    private readonly ICredentialStore _credentialStore;
    private readonly ICameraCredentialStore _cameraCredentialStore;

    public MainWindow(
        MainViewModel viewModel,
        IVideoPlayerService videoPlayerService,
        IOnvifDeviceService onvifDeviceService,
        IOnvifImagingService imagingService,
        IOnvifEventService eventService,
        ICameraProviderResolver providerResolver,
        IVivotekSnapshotService vivotekSnapshotService,
        IVivotekPtzService vivotekPtzService,
        IVivotekParameterService vivotekParameterService,
        ICredentialStore credentialStore,
        ICameraCredentialStore cameraCredentialStore)
    {
        InitializeComponent();

        // DataContext conecta todos los bindings de la ventana con MainViewModel.
        DataContext = viewModel;
        _videoPlayerService = videoPlayerService;
        _onvifDeviceService = onvifDeviceService;
        _imagingService = imagingService;
        _eventService = eventService;
        _providerResolver = providerResolver;
        _vivotekSnapshotService = vivotekSnapshotService;
        _vivotekPtzService = vivotekPtzService;
        _vivotekParameterService = vivotekParameterService;
        _credentialStore = credentialStore;
        _cameraCredentialStore = cameraCredentialStore;

        // La vista LibVLC se enlaza directamente al MediaPlayer desde MainWindow.xaml.
        // No existe un control generado llamado VideoSurface en el XAML actual.

        // Agregamos acciones avanzadas mediante menú contextual para no saturar la UI principal.
        Loaded += (_, _) => ConfigureCameraContextMenu();
    }
