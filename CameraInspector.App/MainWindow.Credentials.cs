using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CameraInspector.App.ViewModels;

namespace CameraInspector.App;

public partial class MainWindow
{
    private Button? _saveCredentialsButton;
    private MainViewModel? _credentialsViewModel;
    private bool _credentialsUiConfigured;

    static MainWindow()
    {
        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnMainWindowLoadedForCredentials));

        EventManager.RegisterClassHandler(
            typeof(DataGrid),
            FrameworkElement.PreviewMouseRightButtonDownEvent,
            new System.Windows.Input.MouseButtonEventHandler(OnDataGridPreviewMouseRightButtonDown));
    }

    private static void OnMainWindowLoadedForCredentials(object sender, RoutedEventArgs e)
    {
        if (sender is not MainWindow window)
            return;

        window.ConfigureCredentialsUi();
    }

    private void ConfigureCredentialsUi()
    {
        if (_credentialsUiConfigured)
            return;

        _saveCredentialsButton = FindButtonByContent(this, "▣ GUARDAR CREDENCIALES");
        if (_saveCredentialsButton is not null)
        {
            _saveCredentialsButton.Command = null;
            _saveCredentialsButton.Click += SaveCredentialsButton_Click;
        }

        if (DataContext is MainViewModel viewModel)
        {
            _credentialsViewModel = viewModel;
            _credentialsViewModel.PropertyChanged += CredentialsViewModel_PropertyChanged;
        }

        _credentialsUiConfigured = true;
        RefreshSaveCredentialsButton();
    }

    private void CredentialsViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.SelectedDevice) or nameof(MainViewModel.StatusText))
            Dispatcher.BeginInvoke(new Action(RefreshSaveCredentialsButton));
    }

    private async void SaveCredentialsButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
            return;

        await viewModel.PromptAndStoreCredentialsAsync();
        RefreshSaveCredentialsButton();
    }

    private void RefreshSaveCredentialsButton()
    {
        if (_saveCredentialsButton is null)
            return;

        _saveCredentialsButton.IsEnabled = DataContext is MainViewModel viewModel
                                           && viewModel.SelectedDevice is not null;
    }

    private static Button? FindButtonByContent(DependencyObject root, string expectedContent)
    {
        var childrenCount = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < childrenCount; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is Button button &&
                string.Equals(button.Content?.ToString(), expectedContent, StringComparison.OrdinalIgnoreCase))
            {
                return button;
            }

            var nested = FindButtonByContent(child, expectedContent);
            if (nested is not null)
                return nested;
        }

        return null;
    }
}
