using System.Windows;
using CameraInspector.App.ViewModels;
using CameraInspector.Core.Interfaces;

namespace CameraInspector.App;

/// <summary>
/// Ventana de administración de red y cámara IP.
/// La vista adapta la guía al fabricante detectado y usa ONVIF como base común cuando está disponible.
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
            ConfigureManufacturerProfileUi();
            _viewModel.BeginLoadingValues();
            try
            {
                await _viewModel.LoadCommand.ExecuteAsync(null);
                await _viewModel.LoadHostnameCommand.ExecuteAsync(null);
            }
            finally
            {
                _viewModel.EndLoadingValues();
            }
        };
    }
}
