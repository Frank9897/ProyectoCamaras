using System.Collections.ObjectModel;
using CameraInspector.Core.Interfaces;
using CameraInspector.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CameraInspector.App;

/// <summary>
/// ViewModel para consultar eventos ONVIF puntualmente.
/// No mantiene una suscripción permanente; cada actualización solicita un lote de mensajes.
/// </summary>
public sealed partial class EventsViewModel : ObservableObject
{
    private readonly DiscoveredDevice _device;
    private readonly IOnvifEventService _eventService;
    private readonly ICredentialStore _credentialStore;
    private readonly ICameraCredentialStore _cameraCredentialStore;

    public ObservableCollection<OnvifEventInfo> Events { get; } = new();

    [ObservableProperty]
    private string _statusText = "Listo para consultar eventos.";

    public event EventHandler? RequestClose;

    public EventsViewModel(
        DiscoveredDevice device,
        IOnvifEventService eventService,
        ICredentialStore credentialStore,
        ICameraCredentialStore cameraCredentialStore)
    {
        // _device identifica la cámara cuya cola de eventos vamos a consultar.
        _device = device;
        // _eventService encapsula PullMessages ONVIF.
        _eventService = eventService;
        // _credentialStore recupera el secreto desde Windows Credential Manager.
        _credentialStore = credentialStore;
        // _cameraCredentialStore localiza la referencia segura de la cámara.
        _cameraCredentialStore = cameraCredentialStore;
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        try
        {
            var credentials = await GetCredentialsAsync();
            if (credentials is null)
                return;

            // messages contiene el lote de eventos devuelto por la cámara.
            var messages = await _eventService.PullMessagesAsync(
                _device,
                credentials.Value.Username,
                credentials.Value.Password,
                timeoutSeconds: 5,
                messageLimit: 20);

            Events.Clear();
            foreach (var message in messages)
                Events.Add(message);

            StatusText = $"Consulta completada: {messages.Count} eventos.";
        }
        catch (Exception ex)
        {
            StatusText = $"Error al consultar eventos: {ex.Message}";
        }
    }

    [RelayCommand]
    private void Close() => RequestClose?.Invoke(this, EventArgs.Empty);

    private async Task<(string Username, string Password)?> GetCredentialsAsync()
    {
        if (_device.CameraId is not int cameraId)
        {
            StatusText = "La cámara aún no está inventariada.";
            return null;
        }

        var savedInfo = await _cameraCredentialStore.GetAsync(cameraId);
        if (savedInfo is null)
        {
            StatusText = "No existen credenciales guardadas para esta cámara.";
            return null;
        }

        var stored = await _credentialStore.GetAsync(savedInfo.CredentialRef);
        if (stored is null)
        {
            StatusText = "La credencial guardada no existe en Windows Credential Manager.";
            return null;
        }

        return (stored.Username, stored.Password);
    }
}
