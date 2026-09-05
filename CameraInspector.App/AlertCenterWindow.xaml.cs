using System.Windows;
using CameraInspector.Core.Interfaces;

namespace CameraInspector.App;

public partial class AlertCenterWindow : Window
{
    private readonly ICameraAlertStore _alertStore;

    public AlertCenterWindow(ICameraAlertStore alertStore)
    {
        InitializeComponent();
        _alertStore = alertStore;
        Loaded += async (_, _) => await RefreshAsync();
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        try
        {
            var alerts = await _alertStore.GetRecentAsync(250);
            AlertsGrid.ItemsSource = alerts;

            var critical = alerts.Count(alert => alert.Severity == "CRÍTICA");
            var high = alerts.Count(alert => alert.Severity == "ALTA");
            var changes = alerts.Count(alert => alert.Type == "CAMBIO HISTÓRICO");
            SummaryText.Text = $"{alerts.Count} eventos · {critical} críticos · {high} altos · {changes} cambios históricos";
        }
        catch (Exception ex)
        {
            SummaryText.Text = $"No se pudo cargar el centro de alertas: {ex.Message}";
        }
    }
}
