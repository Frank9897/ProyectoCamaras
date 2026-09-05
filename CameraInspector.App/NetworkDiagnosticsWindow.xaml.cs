using System.ComponentModel;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.CompilerServices;
using CameraInspector.Core.Models;

namespace CameraInspector.App;

public partial class NetworkDiagnosticsWindow : System.Windows.Window, INotifyPropertyChanged
{
    private readonly NetworkInterfaceInfo _networkInterface;
    private string _testStatus = "Todavía no se ejecutaron pruebas de conectividad.";

    public event PropertyChangedEventHandler? PropertyChanged;

    public string InterfaceName => _networkInterface.Name;
    public string Description => _networkInterface.Description;
    public string LinkState => _networkInterface.IsUp ? "ACTIVA" : "INACTIVA";
    public string MacAddress => _networkInterface.MacAddress ?? "Sin informar";
    public string IpAddress => _networkInterface.IpAddress.ToString();
    public string SubnetDisplay => $"{_networkInterface.SubnetMask} / {_networkInterface.CidrPrefixLength}";
    public string NetworkDisplay => $"{_networkInterface.NetworkAddress}/{_networkInterface.CidrPrefixLength}";
    public string GatewayDisplay => _networkInterface.DefaultGateway?.ToString() ?? "Sin gateway configurado";
    public string DnsDisplay => _networkInterface.DnsServersDisplay;
    public string ConfigurationDisplay => $"{(_networkInterface.UsesDhcp ? "DHCP" : "IP FIJA")} · {(_networkInterface.IsWireless ? "Wi-Fi" : "Ethernet / cableado")}";
    public string TestStatus
    {
        get => _testStatus;
        private set
        {
            if (_testStatus == value) return;
            _testStatus = value;
            OnPropertyChanged();
        }
    }

    public string Interpretation
    {
        get
        {
            if (!_networkInterface.IsUp)
                return "La interfaz figura inactiva. El descubrimiento por esta interfaz no debería considerarse confiable hasta restablecer el enlace.";

            if (_networkInterface.DefaultGateway is null)
                return "La interfaz está activa pero Windows no informa gateway. Esto puede ser normal en una conexión aislada de prueba, pero limita el acceso fuera de la red local.";

            if (_networkInterface.DnsServers.Count == 0)
                return "La interfaz tiene gateway pero no DNS IPv4 informado. El acceso por IP puede funcionar aunque la resolución de nombres falle.";

            return $"La aplicación utilizará {NetworkDisplay} como red objetivo cuando ejecutes RED NORMAL sobre esta interfaz. La IP del PC es {IpAddress}; no son conceptos distintos: la primera identifica la red y la segunda identifica tu equipo dentro de ella.";
        }
    }

    public NetworkDiagnosticsWindow(NetworkInterfaceInfo networkInterface)
    {
        _networkInterface = networkInterface ?? throw new ArgumentNullException(nameof(networkInterface));
        InitializeComponent();
        DataContext = this;
    }

    private async void PingGateway_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        await RunPingSetAsync("gateway", _networkInterface.DefaultGateway is null ? Array.Empty<IPAddress>() : new[] { _networkInterface.DefaultGateway });
    }

    private async void PingDns_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        await RunPingSetAsync("DNS", _networkInterface.DnsServers);
    }

    private async void PingAll_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var targets = new List<IPAddress>();
        if (_networkInterface.DefaultGateway is not null)
            targets.Add(_networkInterface.DefaultGateway);
        targets.AddRange(_networkInterface.DnsServers.Where(dns => !targets.Contains(dns)));
        await RunPingSetAsync("gateway + DNS", targets);
    }

    private async Task RunPingSetAsync(string label, IEnumerable<IPAddress> addresses)
    {
        var targets = addresses.Distinct().ToList();
        if (targets.Count == 0)
        {
            TestStatus = $"No hay objetivos IPv4 disponibles para probar {label}.";
            return;
        }

        TestStatus = $"Probando {label}: {string.Join(", ", targets)}...";
        var results = new List<string>();

        foreach (var address in targets)
        {
            try
            {
                using var ping = new Ping();
                var reply = await ping.SendPingAsync(address, 1200);
                results.Add(reply.Status == IPStatus.Success
                    ? $"{address}: OK ({reply.RoundtripTime} ms)"
                    : $"{address}: {reply.Status}");
            }
            catch (Exception ex)
            {
                results.Add($"{address}: ERROR · {ex.Message}");
            }
        }

        TestStatus = string.Join(" | ", results);
    }

    private void Close_Click(object sender, System.Windows.RoutedEventArgs e) => Close();

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}