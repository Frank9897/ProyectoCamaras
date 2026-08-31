using System.Net.Http;
using System.Windows;
using CameraInspector.App.Security;
using CameraInspector.App.ViewModels;
using CameraInspector.Core.Interfaces;
using CameraInspector.Network;
using CameraInspector.Network.OnvifDiscovery;
using CameraInspector.Network.Providers;
using CameraInspector.Network.Providers.Hikvision;
using CameraInspector.Network.Providers.Vivotek;
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
    private bool _isShuttingDown;

    /// <summary>Proveedor de servicios activo para ventanas auxiliares.</summary>
    public static IServiceProvider? Services { get; private set; }

    public App()
    {
        // El cierre de la ventana principal controla explícitamente el fin del proceso completo.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

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
            _host = Host.CreateDefaultBuilder()
                .ConfigureServices((_, services) =>
                {
                    services.AddDbContextFactory<CameraInspectorDbContext>(options =>
                        options.UseSqlite($"Data Source={CameraInspectorDbContext.GetDefaultDbPath()}"));

                    services.AddSingleton<ICameraInventoryStore, CameraInventoryStore>();
                    services.AddSingleton<IDiagnosticHistoryStore, DiagnosticHistoryStore>();
                    services.AddSingleton<ICameraCredentialStore, CameraCredentialStore>();
                    services.AddSingleton(new HttpClient { Timeout = TimeSpan.FromSeconds(3) });
                    services.AddSingleton<ICredentialStore, WindowsCredentialStore>();

                    services.AddSingleton<INetworkInterfaceService, NetworkInterfaceService>();
                    services.AddSingleton<ISubnetCalculator, SubnetCalculator>();
                    services.AddSingleton<IPingScanner, PingScanner>();
                    services.AddSingleton<IArpResolver, ArpResolver>();
                    services.AddSingleton<IOnvifDiscoveryService, WsDiscoveryOnvifService>();
                    services.AddSingleton<IVivotekDiscoveryService, VivotekDiscoveryService>();
                    services.AddSingleton<INetworkScanner, NetworkScanOrchestrator>();

                    services.AddSingleton<IManufacturerDetector, Network.Detection.OuiMacDetector>();
                    services.AddSingleton<IManufacturerDetector, Network.Detection.HttpBannerDetector>();
                    services.AddSingleton<IManufacturerDetector, Network.Detection.OnvifProbeDetector>();
                    services.AddSingleton<IManufacturerResolver, Network.Detection.ManufacturerResolver>();

                    services.AddSingleton<IOnvifDeviceService, Network.OnvifMedia.OnvifDeviceService>();
                    services.AddSingleton<Network.OnvifMedia.OnvifMediaService>();
                    services.AddSingleton<IOnvifMediaService>(sp =>
                        sp.GetRequiredService<Network.OnvifMedia.OnvifMediaService>());
                    services.AddSingleton<IStreamUriResolver>(sp =>
                        sp.GetRequiredService<Network.OnvifMedia.OnvifMediaService>());
                    services.AddSingleton<IOnvifPtzService, Network.OnvifMedia.OnvifPtzService>();
                    services.AddSingleton<IOnvifImagingService, Network.OnvifMedia.OnvifImagingService>();
                    services.AddSingleton<IOnvifEventService, Network.OnvifMedia.OnvifEventService>();

                    services.AddSingleton<ICameraProvider, HikvisionProvider>();
                    services.AddSingleton<ICameraProvider, VivotekProvider>();
                    services.AddSingleton<CameraProviderResolver>();
                    services.AddSingleton<ICameraProviderResolver>(sp =>
                        sp.GetRequiredService<CameraProviderResolver>());

                    services.AddSingleton<IVivotekSnapshotService, VivotekSnapshotService>();
                    services.AddSingleton<IVivotekPtzService, VivotekPtzService>();
                    services.AddSingleton<IVivotekParameterService, VivotekParameterService>();

                    services.AddSingleton<ICameraDiagnosticService, Network.Diagnostics.CameraDiagnosticService>();
                    services.AddSingleton<IVideoPlayerService, LibVlcVideoPlayerService>();
                    services.AddSingleton<LocalCameraService>();
                    services.AddSingleton<ILocalCameraService>(sp =>
                        sp.GetRequiredService<LocalCameraService>());

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
            mainWindow.Closed += (_, _) => BeginApplicationShutdown();
            mainWindow.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"No se pudo iniciar la aplicación:\n\n{ex}",
                "Camera Inspector — Error de arranque",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            BeginApplicationShutdown(-1);
        }
    }

    /// <summary>
    /// Inicia el cierre completo cuando la ventana principal se cierra.
    /// </summary>
    private void BeginApplicationShutdown(int exitCode = 0)
    {
        if (_isShuttingDown)
            return;

        _isShuttingDown = true;
        Dispatcher.BeginInvoke(new Action(() => Shutdown(exitCode)));
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        _isShuttingDown = true;

        // Cerramos cualquier ventana secundaria todavía visible para liberar controles y recursos nativos.
        for (var index = Windows.Count - 1; index >= 0; index--)
        {
            var window = Windows[index];
            try
            {
                window.Close();
            }
            catch
            {
                // El cierre del proceso debe continuar aunque una ventana ya esté parcialmente destruida.
            }
        }

        // El Generic Host dispone los singletons y libera sockets, HttpClient y servicios de vídeo.
        if (_host is not null)
        {
            try
            {
                await _host.StopAsync();
            }
            finally
            {
                _host.Dispose();
                _host = null;
            }
        }

        Services = null;
        base.OnExit(e);
    }
}
