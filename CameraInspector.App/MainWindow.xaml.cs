using System.Windows;
using CameraInspector.App.ViewModels;

namespace CameraInspector.App;

/// <summary>
/// Code-behind deliberadamente vacío de lógica: en MVVM, la View solo conecta
/// con su ViewModel vía DataContext. Todo lo demás (comandos, datos) vive en MainViewModel.
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
