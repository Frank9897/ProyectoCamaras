using System.Windows;
using CameraInspector.App.ViewModels;
using CameraInspector.Core.Interfaces;

namespace CameraInspector.App;

/// <summary>
/// Ventana auxiliar para consultar información propietaria de la cámara seleccionada.
/// </summary>
public partial class ProviderInfoWindow : Window
{
    public ProviderInfoWindow(
        DeviceViewModel deviceViewModel,
        ICameraProviderResolver providerResolver,
        ICredentialStore credentialStore,
        ICameraCredentialStore cameraCredentialStore)
    {
        InitializeComponent();

        // viewModel conserva CameraId, modelo de red y resolución del provider.
        var viewModel = new ProviderInfoViewModel(
            deviceViewModel,
            providerResolver,
            credentialStore,
            cameraCredentialStore);

        DataContext = viewModel;
        viewModel.RequestClose += (_, _) => Close();

        Loaded += async (_, _) => await viewModel.LoadCommand.ExecuteAsync(null);
    }
}
