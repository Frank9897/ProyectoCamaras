using System.Net.Http;
using System.Windows;
using CameraInspector.App.Security;
using CameraInspector.App.ViewModels;
using CameraInspector.Core.Interfaces;
using CameraInspector.Network;
using CameraInspector.Network.OnvifDiscovery;
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

    public App()
    {
        // Capturamos excepciones no controladas del hilo de UI para mostrar un mensaje visible al técnico.
        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show(
                $"Error no controlado:\n\n{args.Exception}",
                "Camera Inspector — Error de arranque", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        // Capturamos excepciones de dominio que no hayan pasado por el dispatcher de WPF.
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            MessageBox.Show(
                $"Error fatal:\n\n{args.ExceptionObject}",
                "Camera Inspector — Error fatal", MessageBoxButton.OK, MessageBoxImage.Error);
        };
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            // _host contiene el contenedor de inyección de dependencias y el ciclo de vida de la aplicación.
            _host = Host.CreateDefaultBuilder()
                .ConfigureServices((_, services) =>
                {
                    // ---- Persistencia (SQLite) ----
                    services.AddDbContext<CameraInspectorDbContext>(options =>
                        options.UseSqlite($"Data Source={CameraInspectorDbContext.GetDefaultDbPath()}"));

                    // ---- HTTP compartido ----
                    // HttpClient se reutiliza entre diagnósticos para evitar crear un socket nuevo por cada prueba.
                    services.AddSingleton(new HttpClient { Timeout = TimeSpan.FromSeconds(3) });

                    // ---- Seguridad ----
                    // Guarda contraseñas en Windows Credential Manager; SQLite solo conserva CredentialRef.
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

                    // ---- Capa 5: ONVIF Device + Media ----
                    services.AddSingleton<IOnvifDeviceService, Network.OnvifMedia.OnvifDeviceService>();
                    services.AddSingleton<Network.OnvifMedia.OnvifMediaService>();
                    services.AddSingleton<IOnvifMediaService>(sp =>
                        sp.GetRequiredService<Network.OnvifMedia.OnvifMediaService>());
                    services.AddSingleton<IStreamUriResolver>(sp =>
                        sp.GetRequiredService<Network.OnvifMedia.OnvifMediaService>());

                    // ---- Capa 6: Diagnóstico ----
                    services.AddSingleton<ICameraDiagnosticService, Network.Diagnostics.CameraDiagnosticService>();

                    // ---- Capa 7: Video ----
                    // El servicio mantiene LibVLC/MediaPlayer durante toda la aplicación para que el VideoView
                    // pueda reutilizar la misma instancia sin reconstruir el motor multimedia por cada stream.
                    services.AddSingleton<IVideoPlayerService, LibVlcVideoPlayerService>();

                    // ---- ViewModels / Ventanas ----
                    services.AddSingleton<MainViewModel>();
                    services.AddSingleton<MainWindow>();
                })
                .Build();

            await _host.StartAsync();

            // db representa el contexto SQLite utilizado para crear/actualizar la base local.
            using (var scope = _host.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<CameraInspectorDbContext>();
                await db.Database.MigrateAsync();
            }

            // mainWindow contiene la ventana principal y recibe MainViewModel mediante DI.
            var mainWindow = _host.Services.GetRequiredService<MainWindow>();
            mainWindow.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"No se pudo iniciar la aplicación:\n\n{ex}",
                "Camera Inspector — Error de arranque", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(-1);
        }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        // _host puede ser null si el arranque falló antes de construir el contenedor.
        if (_host is not null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }

        base.OnExit(e);
    }
}
