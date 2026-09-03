using System.Net;
using System.Net.Sockets;
using CommunityToolkit.Mvvm.Input;

namespace CameraInspector.App;

public sealed partial class NetworkConfigurationEditViewModel
{
    private bool _loadingValues;

    public bool HasUnsavedChanges { get; private set; }
    public bool IsStatusError => StatusText.StartsWith("ALERTA", StringComparison.OrdinalIgnoreCase)
        || StatusText.StartsWith("Error", StringComparison.OrdinalIgnoreCase)
        || StatusText.StartsWith("No se", StringComparison.OrdinalIgnoreCase)
        || StatusText.Contains("rechaz", StringComparison.OrdinalIgnoreCase);
    public string ValidationMessage { get; private set; } = "";

    partial void OnUseDhcpChanged(bool value)
    {
        if (!_loadingValues) HasUnsavedChanges = true;
        OnPropertyChanged(nameof(HasUnsavedChanges));
        ValidateCurrentNetwork(false);
    }

    partial void OnIpv4AddressChanged(string value)
    {
        if (!_loadingValues) HasUnsavedChanges = true;
        OnPropertyChanged(nameof(HasUnsavedChanges));
        ValidateCurrentNetwork(false);
    }

    partial void OnPrefixLengthChanged(string value)
    {
        if (!_loadingValues) HasUnsavedChanges = true;
        OnPropertyChanged(nameof(HasUnsavedChanges));
        ValidateCurrentNetwork(false);
    }

    partial void OnGatewayAddressChanged(string value)
    {
        if (!_loadingValues) HasUnsavedChanges = true;
        OnPropertyChanged(nameof(HasUnsavedChanges));
        ValidateCurrentNetwork(false);
    }

    public void BeginLoadingValues() => _loadingValues = true;
    public void EndLoadingValues()
    {
        _loadingValues = false;
        HasUnsavedChanges = false;
        OnPropertyChanged(nameof(HasUnsavedChanges));
        ValidateCurrentNetwork(false);
    }

    partial void OnStatusTextChanged(string value)
        => OnPropertyChanged(nameof(IsStatusError));

    [RelayCommand]
    private void ValidateNetwork()
    {
        if (ValidateCurrentNetwork(true))
            StatusText = "OK: configuración IPv4 válida y lista para aplicar.";
        else
            StatusText = $"ALERTA: {ValidationMessage}";
    }

    private bool ValidateCurrentNetwork(bool requireInterface)
    {
        if (requireInterface && SelectedInterface is null)
        {
            ValidationMessage = "Seleccione una interfaz de red.";
            OnPropertyChanged(nameof(ValidationMessage));
            return false;
        }

        if (!UseDhcp)
        {
            if (!IPAddress.TryParse(Ipv4Address?.Trim(), out var ip) || ip.AddressFamily != AddressFamily.InterNetwork)
            {
                ValidationMessage = "La IPv4 no es válida. Ejemplo: 192.168.1.50.";
                OnPropertyChanged(nameof(ValidationMessage));
                return false;
            }

            if (!int.TryParse(PrefixLength?.Trim(), out var prefix) || prefix < 1 || prefix > 32)
            {
                ValidationMessage = "El prefijo CIDR debe estar entre 1 y 32.";
                OnPropertyChanged(nameof(ValidationMessage));
                return false;
            }

            if (IsBroadcastAddress(ip, prefix))
            {
                ValidationMessage = "La IPv4 indicada corresponde a una dirección de broadcast para ese prefijo.";
                OnPropertyChanged(nameof(ValidationMessage));
                return false;
            }
        }

        if (!string.IsNullOrWhiteSpace(GatewayAddress))
        {
            if (!IPAddress.TryParse(GatewayAddress.Trim(), out var gateway) || gateway.AddressFamily != AddressFamily.InterNetwork)
            {
                ValidationMessage = "El gateway debe ser una dirección IPv4 válida.";
                OnPropertyChanged(nameof(ValidationMessage));
                return false;
            }

            if (!UseDhcp && IPAddress.TryParse(Ipv4Address?.Trim(), out var ip) && int.TryParse(PrefixLength, out var prefix)
                && !SameSubnet(ip, gateway, prefix))
            {
                ValidationMessage = "El gateway no pertenece a la misma subred que la IPv4 configurada.";
                OnPropertyChanged(nameof(ValidationMessage));
                return false;
            }
        }

        ValidationMessage = string.IsNullOrWhiteSpace(GatewayAddress)
            ? "IPv4 válida. No se configurará gateway manual."
            : "IPv4, prefijo y gateway válidos.";
        OnPropertyChanged(nameof(ValidationMessage));
        return true;
    }

    private static bool SameSubnet(IPAddress first, IPAddress second, int prefix)
    {
        var a = first.GetAddressBytes();
        var b = second.GetAddressBytes();
        var bytes = prefix / 8;
        var remaining = prefix % 8;
        for (var i = 0; i < bytes; i++)
            if (a[i] != b[i]) return false;
        if (remaining == 0) return true;
        var mask = (byte)(0xFF << (8 - remaining));
        return (a[bytes] & mask) == (b[bytes] & mask);
    }

    private static bool IsBroadcastAddress(IPAddress address, int prefix)
    {
        if (prefix >= 32) return false;
        var value = address.GetAddressBytes();
        var hostBits = 32 - prefix;
        var fullHostBytes = hostBits / 8;
        var remaining = hostBits % 8;
        for (var i = 0; i < fullHostBytes; i++)
            if (value[3 - i] != 0xFF) return false;
        if (remaining == 0) return false;
        var mask = (byte)((1 << remaining) - 1);
        return (value[3 - fullHostBytes] & mask) == mask;
    }
}
