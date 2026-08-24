using System.Windows;
using CameraInspector.App.ViewModels;
using CameraInspector.Video;

namespace CameraInspector.App;

/// <summary>
/// Code-behind mínimo de la ventana.
/// Su única responsabilidad adicional es conectar el MediaPlayer del servicio de video
/// con el control visual VideoView de LibVLCSharp.
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
    }
}
