using CameraInspector.App.ViewModels;

namespace CameraInspector.App;

public partial class ScanProfilesWindow : System.Windows.Window
{
    private readonly MainViewModel _viewModel;

    public ScanProfilesWindow(MainViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();
        DataContext = viewModel;
    }

    private async void Direct_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (_viewModel.IsScanning) return;
        await _viewModel.ScanDirectCameraCommand.ExecuteAsync(null);
    }

    private async void Subnet_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (_viewModel.IsScanning) return;
        await _viewModel.ScanNetworkSubnetCommand.ExecuteAsync(null);
    }

    private async void Full_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (_viewModel.IsScanning) return;
        await _viewModel.ScanFullNetworkCommand.ExecuteAsync(null);
    }

    private void Close_Click(object sender, System.Windows.RoutedEventArgs e) => Close();
}