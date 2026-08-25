using System.Windows;
using CameraInspector.App.ViewModels;
using CameraInspector.Core.Interfaces;

namespace CameraInspector.App;

/// <summary>
/// Ventana de consulta de red ONVIF.
/// La interfaz actual es deliberadamente de solo lectura para evitar cambios accidentales.
/// </summary>
public partial class NetworkConfigurationWindow : Window
{
    public NetworkConfigurationWindow(
        DeviceViewModel deviceViewModel,
        IOnvifDeviceService onvifDeviceService,
        ICredentialStore credentialStore,
        ICameraCredentialStore cameraCredentialStore)
    {
        InitializeComponent();

        // viewModel conserva la identidad de inventario y coordina la consulta autenticada.
        var viewModel = new NetworkConfigurationViewModel(
            deviceViewModel,
            onvifDeviceService,
            credentialStore,
            cameraCredentialStore);

        DataContext = viewModel;
        viewModel.RequestClose += (_, _) => Close();

        // La ventana intenta cargar la configuración al abrirse; no ejecuta ninguna escritura.
        Loaded += async (_, _) => await viewModel.LoadCommand.ExecuteAsync(null);
    }
}
