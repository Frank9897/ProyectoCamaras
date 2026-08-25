using System.Windows;
using CameraInspector.App.ViewModels;
using CameraInspector.Core.Interfaces;

namespace CameraInspector.App;

/// <summary>
/// Ventana de inspección de parámetros CGI VIVOTEK en modo lectura.
/// </summary>
public partial class VivotekParametersWindow : Window
{
    public VivotekParametersWindow(
        DeviceViewModel deviceViewModel,
        IVivotekParameterService parameterService,
        ICredentialStore credentialStore,
        ICameraCredentialStore cameraCredentialStore)
    {
        InitializeComponent();

        // viewModel conserva el dispositivo seleccionado y coordina las consultas CGI.
        var viewModel = new VivotekParametersViewModel(
            deviceViewModel,
            parameterService,
            credentialStore,
            cameraCredentialStore);

        DataContext = viewModel;
        viewModel.RequestClose += (_, _) => Close();

        Loaded += async (_, _) => await viewModel.LoadCommand.ExecuteAsync(null);
    }
}
