using System.IO;
using System.Net.Http;
using System.Text;
using System.Windows;
using CameraInspector.App.Responsive;
using CameraInspector.App.Security;
using CameraInspector.App.ViewModels;
using CameraInspector.Core.Interfaces;
using CameraInspector.Network;
using CameraInspector.Network.OnvifDiscovery;
using CameraInspector.Network.Providers;
using CameraInspector.Network.Providers.Hikvision;
using CameraInspector.Network.Providers.Reolink;
using CameraInspector.Network.Providers.Vivotek;
using CameraInspector.Persistence;
using CameraInspector.Video;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CameraInspector.App;

public partial class App : Application
{
    private IHost? _host;
    private bool _isShuttingDown;
    private static bool _responsiveWindowsRegistered;
    public static IServiceProvider? Services { get; private set; }

    // En una publicación single-file AppContext.BaseDirectory puede apuntar al directorio
    // temporal donde .NET extrae componentes nativos. El log debe quedar junto al EXE real.
    private static string ErrorLogPath
    {
        get
        {
            try
            {
                var processPath = Environment.ProcessPath;
                var executableDirectory = string.IsNullOrWhiteSpace(processPath)
                    ? null
                    : Path.GetDirectoryName(processPath);

                return Path.Combine(
                    executableDirectory ?? AppContext.BaseDirectory,
                    "CameraInspector_error.txt");
            }
            catch
            {
                return Path.Combine(AppContext.BaseDirectory, "CameraInspector_error.txt");
            }
        }
    }

    public App()
    {
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        RegisterResponsiveWindowHandling();
        DispatcherUnhandledException += (_, args) =>
        {
            WriteErrorLog("EXCEPCIÓN NO CONTROLADA EN UI", args.Exception);
            MessageBox.Show($"Ocurrió un error no controlado.\n\nSe guardó el detalle en:\n{ErrorLogPath}\n\n{args.Exception.Message}", "Camera Inspector — Error", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) => WriteErrorLog("EXCEPCIÓN FATAL", args.ExceptionObject);
    }

    private static void RegisterResponsiveWindowHandling()
    {
        if (_responsiveWindowsRegistered)
            return;

        _responsiveWindowsRegistered = true;
        EventManager.RegisterClassHandler(
            typeof(Window),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(ApplyResponsiveWindowBehavior));
    }

    private static void ApplyResponsiveWindowBehavior(object sender, RoutedEventArgs e)
    {
        if (sender is Window window)
            ResponsiveWindowBehavior.SetEnable(window, true);
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            _host = Host.CreateDefaultBuilder()
                .ConfigureServices((_, services) =>
                {
                    services.AddDbContextFactory<CameraInspectorDbContext>(options => options.UseSqlite($"Data Source={CameraInspectorDbContext.GetDefaultDbPath()}"));
                    services.AddSingleton<ICameraInventoryStore, CameraInventoryStore>();
                    services.AddSingleton<IDiagnosticHistoryStore, DiagnosticHistoryStore>();
                    services.AddSingleton<ICameraAlertStore, CameraAlertStore>();
                    services.AddSingleton<ICameraCredentialStore, CameraCredentialStore>();
                    services.AddSingleton(new HttpClient { Timeout = TimeSpan.FromSeconds(3) });
                    services.AddSingleton<ICredentialStore, WindowsCredentialStore>();

                    services.AddSingleton<INetworkInterfaceService, NetworkInterfaceService>();
                    services.AddSingleton<ISubnetCalculator, SubnetCalculator>();
                    services.AddSingleton<IPingScanner, PingScanner>();
                    services.AddSingleton<IArpResolver, ArpResolver>();
                    services.AddSingleton<IOnvifDiscoveryService, WsDiscoveryOnvifService>();
                    services.AddSingleton<IVivotekDiscoveryService, VivotekDiscoveryService>();
                    services.AddSingleton<ReolinkDiscoveryService>();
                    services.AddSingleton<CameraPortScanner>();
                    services.AddSingleton<SsdpDiscoveryService>();
                    services.AddSingleton<LegacyVendorDiscoveryService>();
                    services.AddSingleton<MdnsDiscoveryService>();
                    services.AddSingleton<INetworkScanner, NetworkScanOrchestrator>();

                    services.AddSingleton<ICameraHealthService, Network.Diagnostics.CameraHealthService>();

                    services.AddSingleton<IManufacturerDetector, Network.Detection.OuiMacDetector>();
                    services.AddSingleton<IManufacturerDetector, Network.Detection.HttpBannerDetector>();
                    services.AddSingleton<IManufacturerDetector, Network.Detection.LegacyCameraHttpDetector>();
                    services.AddSingleton<IManufacturerDetector, Network.Detection.RtspFingerprintDetector>();
                    services.AddSingleton<IManufacturerDetector, Network.Detection.GenericVideoHttpDetector>();
                    services.AddSingleton<IManufacturerDetector, Network.Detection.OnvifProbeDetector>();
                    services.AddSingleton<IManufacturerResolver, Network.Detection.ManufacturerResolver>();

                    services.AddSingleton<IOnvifDeviceService, Network.OnvifMedia.OnvifDeviceService>();
                    services.AddSingleton<Network.OnvifMedia.OnvifMediaService>();
                    services.AddSingleton<IOnvifMediaService>(sp => sp.GetRequiredService<Network.OnvifMedia.OnvifMediaService>());
                    services.AddSingleton<IStreamUriResolver>(sp => sp.GetRequiredService<Network.OnvifMedia.OnvifMediaService>());
                    services.AddSingleton<IOnvifPtzService, Network.OnvifMedia.OnvifPtzService>();
                    services.AddSingleton<IOnvifImagingService, Network.OnvifMedia.OnvifImagingService>();
                    services.AddSingleton<IOnvifEventService, Network.OnvifMedia.OnvifEventService>();

                    services.AddSingleton<ICameraProvider, HikvisionProvider>();
                    services.AddSingleton<ICameraProvider, VivotekProvider>();
                    services.AddSingleton<CameraProviderResolver>();
                    services.AddSingleton<ICameraProviderResolver>(sp => sp.GetRequiredService<CameraProviderResolver>());

                    services.AddSingleton<IVivotekSnapshotService, VivotekSnapshotService>();
                    services.AddSingleton<IVivotekPtzService, VivotekPtzService>();
                    services.AddSingleton<IVivotekParameterService, VivotekParameterService>();

                    services.AddSingleton<ICameraDiagnosticService, Network.Diagnostics.CameraDiagnosticService>();
                    services.AddSingleton<IVideoPlayerService, LibVlcVideoPlayerService>();
                    services.AddSingleton<LocalCameraService>();
                    services.AddSingleton<ILocalCameraService>(sp => sp.GetRequiredService<LocalCameraService>());
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
            WriteErrorLog("ERROR DE ARRANQUE", ex);
            MessageBox.Show($"No se pudo iniciar la aplicación.\n\nSe guardó el detalle en:\n{ErrorLogPath}\n\n{ex.Message}", "Camera Inspector — Error de arranque", MessageBoxButton.OK, MessageBoxImage.Error);
            BeginApplicationShutdown(-1);
        }
    }

    private static void WriteErrorLog(string title, object? error)
    {
        try
        {
            var sb = new StringBuilder()
                .AppendLine("============================================================")
                .AppendLine($"Camera Inspector — {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}")
                .AppendLine(title)
                .AppendLine("============================================================")
                .AppendLine($"OS: {Environment.OSVersion}")
                .AppendLine($"64-bit OS: {Environment.Is64BitOperatingSystem}")
                .AppendLine($"64-bit proceso: {Environment.Is64BitProcess}")
                .AppendLine($"Proceso: {Environment.ProcessPath ?? "desconocido"}")
                .AppendLine($"BaseDirectory .NET: {AppContext.BaseDirectory}")
                .AppendLine($"Log: {ErrorLogPath}")
                .AppendLine();
            sb.AppendLine(error is Exception exception ? exception.ToString() : error?.ToString() ?? "Error desconocido.");
            File.AppendAllText(ErrorLogPath, sb.ToString() + Environment.NewLine, Encoding.UTF8);
        }
        catch { }
    }

    private void BeginApplicationShutdown(int exitCode = 0)
    {
        if (_isShuttingDown) return;
        _isShuttingDown = true;
        Dispatcher.BeginInvoke(new Action(() => Shutdown(exitCode)));
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        _isShuttingDown = true;

        try
        {
            await VivotekDirectLinkAddressService.RemoveTemporaryAddressesAsync();
        }
        catch
        {
        }

        for (var index = Windows.Count - 1; index >= 0; index--)
        {
            try { Windows[index].Close(); } catch { }
        }
        if (_host is not null)
        {
            try { await _host.StopAsync(); }
            finally { _host.Dispose(); _host = null; }
        }
        Services = null;
        base.OnExit(e);
    }
}