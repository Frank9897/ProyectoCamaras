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

        var services = App.Services;
        if (services is null)
        {
            viewModel.StatusText = "ALERTA: los servicios de la aplicación no están disponibles.";
            return;
        }

        try
        {
            var manufacturer = viewModel.SelectedDevice.Manufacturer ?? string.Empty;

            // VIVOTEK IP7133 y otros modelos legacy pueden ofrecer imagen por CGI
            // aunque no anuncien Imaging ONVIF. En ese caso abrimos el inspector propietario.
            if (manufacturer.Contains("VIVOTEK", StringComparison.OrdinalIgnoreCase))
            {
                new VivotekParametersWindow(
                    viewModel.SelectedDevice,
                    services.GetRequiredService<IVivotekParameterService>(),
                    services.GetRequiredService<ICredentialStore>(),
                    services.GetRequiredService<ICameraCredentialStore>())
                {
                    Owner = this,
                    ShowInTaskbar = false
                }.ShowDialog();
                return;
            }

            if (!viewModel.SelectedDevice.HasImagingService)
            {
                viewModel.StatusText = "ALERTA: esta cámara no anunció Imaging ONVIF. El vídeo puede funcionar por un protocolo propietario y esta operación concreta no está disponible por ONVIF.";
                return;
            }

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
            viewModel.StatusText = $"ALERTA: no se pudo abrir ajustes de imagen: {ex.Message}";
        }
    }

    private void EventsButton_Click(object sender, RoutedEventArgs e)
    {
        var viewModel = _viewModel;
        if (viewModel.SelectedDevice is null)
            return;

        var services = App.Services;
        if (services is null)
        {
            viewModel.StatusText = "ALERTA: los servicios de la aplicación no están disponibles.";
            return;
        }

        if (!viewModel.SelectedDevice.HasEventsService)
        {
            viewModel.StatusText = "ALERTA: Eventos no disponibles porque la cámara no anunció Events ONVIF.";
            return;
        }

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
            viewModel.StatusText = $"ALERTA: no se pudo abrir eventos: {ex.Message}";
        }
    }

    private void ProviderButton_Click(object sender, RoutedEventArgs e)
    {
        var viewModel = _viewModel;
        if (viewModel.SelectedDevice is null)
            return;

        var services = App.Services;
        if (services is null)
        {
            viewModel.StatusText = "ALERTA: los servicios de la aplicación no están disponibles.";
            return;
        }

        try
        {
            var resolver = services.GetRequiredService<ICameraProviderResolver>();
            if (resolver.Resolve(viewModel.SelectedDevice.Device) is null)
            {
                viewModel.StatusText = "ALERTA: no hay un provider propietario compatible con esta cámara.";
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
            viewModel.StatusText = $"ALERTA: no se pudo abrir funciones propietarias: {ex.Message}";
        }
    }
}
