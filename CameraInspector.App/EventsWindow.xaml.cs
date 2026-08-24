using System.Windows;
using CameraInspector.App.ViewModels;
using CameraInspector.Core.Interfaces;

namespace CameraInspector.App;

/// <summary>
/// Ventana auxiliar para consultar eventos ONVIF de la cámara seleccionada.
/// </summary>
public partial class EventsWindow : Window
{
    public EventsWindow(
        DeviceViewModel deviceViewModel,
        IOnvifEventService eventService,
        ICredentialStore credentialStore,
        ICameraCredentialStore cameraCredentialStore)
    {
        InitializeComponent();

        var viewModel = new EventsViewModel(
            deviceViewModel.Device,
            eventService,
            credentialStore,
            cameraCredentialStore);

        DataContext = viewModel;
        viewModel.RequestClose += (_, _) => Close();

        Loaded += async (_, _) => await viewModel.RefreshCommand.ExecuteAsync(null);
    }
}
