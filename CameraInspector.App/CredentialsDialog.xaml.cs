using System.Windows;

namespace CameraInspector.App;

public partial class CredentialsDialog : Window
{
    public string Username { get; private set; } = "";
    public string Password { get; private set; } = "";

    public CredentialsDialog()
    {
        InitializeComponent();
    }

    private void OnAccept(object sender, RoutedEventArgs e)
    {
        Username = UsernameBox.Text;
        Password = PasswordBox.Password;
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
