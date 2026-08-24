using System.Windows;
using CameraInspector.App.ViewModels;

namespace CameraInspector.App;

public partial class PtzWindow : Window
{
    public PtzWindow(MainViewModel viewModel)
    {
        // DataContext permite reutilizar los comandos PTZ del ViewModel principal sin duplicar lógica.
        DataContext = viewModel;
        InitializeComponent();
    }
}
