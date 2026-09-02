using System.Windows;
using CameraInspector.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace CameraInspector.App;

public partial class IpCameraVideoWindow
{
    private void ImagingButton_Click(object sender, RoutedEventArgs e)
    {
        var viewModel = _viewModel;
        if (viewModel.SelectedDevice is null)
            return;

        if (!viewModel.SelectedDevice.HasImagingService)
        {
            viewModel.StatusText = "Imagen no disponible: la cámara no anunció Imaging ONVIF.";
            return;
        }

        var services = App.Services;
        if (services is null)
            return;

        try
        {
            new ImagingWindow(
                viewModel.SelectedDevice,
                services.GetRequiredService<IOnvifImagingService>(),
                services.GetRequiredService<ICredentialStore>(),
                services.GetRequiredService<ICameraCredentialStore>())
            {
                Owner = this,
                ShowInTaskbar = false
            }.ShowDialog();
        }
        catch (Exception ex)
        {
            viewModel.StatusText = $"No se pudo abrir ajustes de imagen: {ex.Message}";
        }
    }

    private void EventsButton_Click(object sender, RoutedEventArgs e)
    {
        var viewModel = _viewModel;
        if (viewModel.SelectedDevice is null)
            return;

        if (!viewModel.SelectedDevice.HasEventsService)
        {
            viewModel.StatusText = "Eventos no disponibles: la cámara no anunció Events ONVIF.";
            return;
        }

        var services = App.Services;
        if (services is null)
            return;

        try
        {
            new EventsWindow(
                viewModel.SelectedDevice,
                services.GetRequiredService<IOnvifEventService>(),
                services.GetRequiredService<ICredentialStore>(),
                services.GetRequiredService<ICameraCredentialStore>())
            {
                Owner = this,
                ShowInTaskbar = false
            }.ShowDialog();
        }
        catch (Exception ex)
        {
            viewModel.StatusText = $"No se pudo abrir eventos: {ex.Message}";
        }
    }

    private void ProviderButton_Click(object sender, RoutedEventArgs e)
    {
        var viewModel = _viewModel;
        if (viewModel.SelectedDevice is null)
            return;

        var services = App.Services;
        if (services is null)
            return;

        try
        {
            var resolver = services.GetRequiredService<ICameraProviderResolver>();
            if (resolver.Resolve(viewModel.SelectedDevice.Device) is null)
            {
                viewModel.StatusText = "No hay un provider propietario compatible con esta cámara.";
                return;
            }

            new ProviderInfoWindow(
                viewModel.SelectedDevice,
                resolver,
                services.GetRequiredService<ICredentialStore>(),
                services.GetRequiredService<ICameraCredentialStore>())
            {
                Owner = this,
                ShowInTaskbar = false
            }.ShowDialog();
        }
        catch (Exception ex)
        {
            viewModel.StatusText = $"No se pudo abrir funciones propietarias: {ex.Message}";
        }
    }
}
