using CameraInspector.App.ViewModels;

namespace CameraInspector.App;

public partial class CameraTechnicalSheetWindow : System.Windows.Window
{
    public CameraTechnicalSheetWindow(DeviceViewModel device)
    {
        ArgumentNullException.ThrowIfNull(device);
        InitializeComponent();
        DataContext = device;
    }

    private void Close_Click(object sender, System.Windows.RoutedEventArgs e) => Close();
}