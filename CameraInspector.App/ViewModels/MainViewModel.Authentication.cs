using CommunityToolkit.Mvvm.ComponentModel;

namespace CameraInspector.App.ViewModels;

public sealed partial class MainViewModel
{
    /// <summary>
    /// Indica si el acceso al stream requiere autenticación.
    /// Se activa únicamente después de que el intento sin credenciales no consigue iniciar el video.
    /// </summary>
    [ObservableProperty]
    private bool _authenticationRequired;
}
