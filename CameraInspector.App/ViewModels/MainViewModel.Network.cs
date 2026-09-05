using CameraInspector.Core.Models;
using CommunityToolkit.Mvvm.Input;

namespace CameraInspector.App.ViewModels;

public sealed partial class MainViewModel
{
    /// <summary>
    /// Vuelve a consultar Windows para que el técnico pueda revisar el estado de red
    /// sin reiniciar la aplicación al conectar o desconectar un adaptador.
    /// </summary>
    [RelayCommand]
    private void RefreshNetworkInterfaces()
    {
        var previousId = SelectedInterface?.InterfaceId;
        var interfaces = _interfaceService.GetActiveInterfaces();

        AvailableInterfaces.Clear();
        foreach (var networkInterface in interfaces)
            AvailableInterfaces.Add(networkInterface);

        SelectedInterface = AvailableInterfaces.FirstOrDefault(item => item.InterfaceId == previousId)
            ?? AvailableInterfaces.FirstOrDefault();

        StatusText = SelectedInterface is null
            ? "ALERTA: no hay interfaces IPv4 activas disponibles para el descubrimiento."
            : $"Red local actualizada: {SelectedInterface.Name} · {SelectedInterface.IpAddress}/{SelectedInterface.CidrPrefixLength}.";
    }

    partial void OnSelectedInterfaceChanged(NetworkInterfaceInfo? value)
    {
        if (value is not null && !IsScanning)
            StatusText = $"Interfaz seleccionada: {value.Name} · red {value.NetworkAddress}/{value.CidrPrefixLength}.";
    }
}
