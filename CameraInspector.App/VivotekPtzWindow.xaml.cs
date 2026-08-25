using System.Windows;
using CameraInspector.App.ViewModels;
using CameraInspector.Core.Interfaces;

namespace CameraInspector.App;

/// <summary>
/// Ventana auxiliar para controlar PTZ mediante CGI propietario de VIVOTEK.
/// </summary>
public partial class VivotekPtzWindow : Window
{
    public VivotekPtzWindow(
        DeviceViewModel deviceViewModel,
        IVivotekPtzService ptzService,
        ICredentialStore credentialStore,
        ICameraCredentialStore cameraCredentialStore)
    {
        InitializeComponent();

        // viewModel conserva la identidad de inventario y delega cada comando al servicio VIVOTEK.
        var viewModel = new VivotekPtzViewModel(
            deviceViewModel,
            ptzService,
            credentialStore,
            cameraCredentialStore);

        DataContext = viewModel;
        viewModel.RequestClose += (_, _) => Close();
    }
}
