using System.Windows;
using CameraInspector.App.ViewModels;
using CameraInspector.Core.Interfaces;

namespace CameraInspector.App;

/// <summary>
/// Ventana auxiliar para operar ajustes Imaging sin sobrecargar MainWindow.
/// </summary>
public partial class ImagingWindow : Window
{
    public ImagingWindow(
        DeviceViewModel deviceViewModel,
        IOnvifImagingService imagingService,
        ICredentialStore credentialStore,
        ICameraCredentialStore cameraCredentialStore)
    {
        InitializeComponent();

        // El ViewModel recibe el DeviceViewModel completo para conservar CameraId y el modelo de red.
        var viewModel = new ImagingViewModel(
            deviceViewModel,
            imagingService,
            credentialStore,
            cameraCredentialStore);

        DataContext = viewModel;
        viewModel.RequestClose += (_, _) => Close();

        Loaded += async (_, _) => await viewModel.LoadCommand.ExecuteAsync(null);
    }
}
