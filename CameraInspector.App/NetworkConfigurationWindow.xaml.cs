using System.Windows;
using CameraInspector.App.ViewModels;
using CameraInspector.Core.Interfaces;

namespace CameraInspector.App;

/// <summary>
/// Ventana de administración de red ONVIF.
/// La lectura es automática; las escrituras requieren confirmación explícita.
/// </summary>
public partial class NetworkConfigurationWindow : Window
{
    private readonly NetworkConfigurationEditViewModel _viewModel;

    public NetworkConfigurationWindow(
        DeviceViewModel deviceViewModel,
        IOnvifDeviceService onvifDeviceService,
        ICredentialStore credentialStore,
        ICameraCredentialStore cameraCredentialStore)
    {
        InitializeComponent();

        _viewModel = new NetworkConfigurationEditViewModel(
            deviceViewModel,
            onvifDeviceService,
            credentialStore,
            cameraCredentialStore);

        DataContext = _viewModel;
        _viewModel.RequestClose += (_, _) => Close();

        Loaded += async (_, _) =>
        {
            await _viewModel.LoadCommand.ExecuteAsync(null);
            await _viewModel.LoadHostnameCommand.ExecuteAsync(null);
        };
    }
}
