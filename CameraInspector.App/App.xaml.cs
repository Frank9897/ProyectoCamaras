using System.Net.Http;
using System.Windows;
using CameraInspector.App.Security;
using CameraInspector.App.ViewModels;
using CameraInspector.Core.Interfaces;
using CameraInspector.Network;
using CameraInspector.Network.OnvifDiscovery;
using CameraInspector.Network.Providers;
using CameraInspector.Network.Providers.Hikvision;
using CameraInspector.Persistence;
using CameraInspector.Video;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CameraInspector.App;

/// <summary>
/// Punto de arranque de la aplicación.
/// El Generic Host centraliza DI, persistencia y ciclo de vida de los servicios.
/// </summary>
public partial class App : Application
{
    private IHost? _host;

    /// <summary>Proveedor de servicios activo para ventanas auxiliares.</summary>
    public static IServiceProvider? Services { get; private set; }

    public App()
    {
        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show($"Error no controlado:\n\n{args.Exception}",
                "Camera Inspector — Error de arranque",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            MessageBox.Show($"Error fatal:\n\n{args.ExceptionObject}",
                "Camera Inspector — Error fatal",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        };
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            // _host contiene el contenedor DI y gestiona el ciclo de vida de los servicios.
            _host = Host.CreateDefaultBuilder()
                .ConfigureServices((_, services) =>
                {
                    // ---- Persistencia (SQLite) ----
                    services.AddDbContextFactory<CameraInspectorDbContext>(options =>
                        options.UseSqlite($"Data Source={CameraInspectorDbContext.GetDefaultDbPath()}"));

                    services.AddSingleton<ICameraInventoryStore, CameraInventoryStore>();
                    services.AddSingleton<IDiagnosticHistoryStore, DiagnosticHistoryStore>();
                    services.AddSingleton<ICameraCredentialStore, CameraCredentialStore>();

                    // ---- HTTP compartido ----
                    services.AddSingleton(new HttpClient { Timeout = TimeSpan.FromSeconds(3) });

                    // ---- Seguridad ----
                    services.AddSingleton<ICredentialStore, WindowsCredentialStore>();

                    // ---- Capa 3: Descubrimiento ----
                    services.AddSingleton<INetworkInterfaceService, NetworkInterfaceService>();
                    services.AddSingleton<ISubnetCalculator, SubnetCalculator>();
                    services.AddSingleton<IPingScanner, PingScanner>();
                    services.AddSingleton<IArpResolver, ArpResolver>();
                    services.AddSingleton<IOnvifDiscoveryService, WsDiscoveryOnvifService>();
                    services.AddSingleton<INetworkScanner, NetworkScanOrchestrator>();

                    // ---- Capa 4: Resolución de fabricante ----
                    services.AddSingleton<IManufacturerDetector, Network.Detection.OuiMacDetector>();
                    services.AddSingleton<IManufacturerDetector, Network.Detection.HttpBannerDetector>();
                    services.AddSingleton<IManufacturerDetector, Network.Detection.OnvifProbeDetector>();
                    services.AddSingleton<IManufacturerResolver, Network.Detection.ManufacturerResolver>();

                    // ---- Capa 5: ONVIF Device + Media + PTZ + Imaging + Events ----
                    services.AddSingleton<IOnvifDeviceService, Network.OnvifMedia.OnvifDeviceService>();
                    services.AddSingleton<Network.OnvifMedia.OnvifMediaService>();
                    services.AddSingleton<IOnvifMediaService>(sp =>
                        sp.GetRequiredService<Network.OnvifMedia.OnvifMediaService>());
                    services.AddSingleton<IStreamUriResolver>(sp =>
                        sp.GetRequiredService<Network.OnvifMedia.OnvifMediaService>());
                    services.AddSingleton<IOnvifPtzService, Network.OnvifMedia.OnvifPtzService>();
                    services.AddSingleton<IOnvifImagingService, Network.OnvifMedia.OnvifImagingService>();
                    services.AddSingleton<IOnvifEventService, Network.OnvifMedia.OnvifEventService>();

                    // ---- Providers propietarios ----
                    // Los providers se evalúan por evidencia antes de realizar operaciones autenticadas.
                    services.AddSingleton<ICameraProvider, HikvisionProvider>();
                    services.AddSingleton<CameraProviderResolver>();
                    services.AddSingleton<ICameraProviderResolver>(sp =>
                        sp.GetRequiredService<CameraProviderResolver>());

                    // ---- Capa 6: Diagnóstico ----
                    services.AddSingleton<ICameraDiagnosticService, Network.Diagnostics.CameraDiagnosticService>();

                    // ---- Capa 7: Video ----
                    services.AddSingleton<IVideoPlayerService, LibVlcVideoPlayerService>();

                    services.AddSingleton<MainViewModel>();
                    services.AddSingleton<MainWindow>();
                })
                .Build();

            await _host.StartAsync();
            Services = _host.Services;

            await using (var scope = _host.Services.CreateAsyncScope())
            {
                var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<CameraInspectorDbContext>>();
                await using var db = await factory.CreateDbContextAsync();
                await db.Database.MigrateAsync();
            }

            var mainWindow = _host.Services.GetRequiredService<MainWindow>();
            mainWindow.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"No se pudo iniciar la aplicación:\n\n{ex}",
                "Camera Inspector — Error de arranque",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(-1);
        }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }

        Services = null;
        base.OnExit(e);
    }
}
