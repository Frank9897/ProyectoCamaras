using CommunityToolkit.Mvvm.ComponentModel;

namespace CameraInspector.App.ViewModels;

public sealed partial class MainViewModel
{
    /// <summary>
    /// Indica si existe una grabación RTSP activa en el reproductor secundario.
    /// </summary>
    [ObservableProperty]
    private bool _isRecording;
}
