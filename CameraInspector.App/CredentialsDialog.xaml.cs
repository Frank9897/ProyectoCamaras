using System.Windows;

namespace CameraInspector.App;

/// <summary>
/// Diálogo simple para capturar las credenciales que el técnico decide utilizar.
/// La ventana no sabe cómo se almacenan; solo informa al ViewModel si deben guardarse.
/// </summary>
public partial class CredentialsDialog : Window
{
    /// <summary>Usuario capturado por el técnico.</summary>
    public string Username { get; private set; } = string.Empty;

    /// <summary>
    /// Contraseña capturada. Existe únicamente en memoria durante la operación actual.
    /// </summary>
    public string Password { get; private set; } = string.Empty;

    /// <summary>Indica si el técnico solicitó guardar la credencial en el almacén seguro.</summary>
    public bool SaveCredential { get; private set; }

    public CredentialsDialog()
    {
        InitializeComponent();
    }

    /// <summary>Inicializa el usuario para evitar que el técnico tenga que escribirlo nuevamente.</summary>
    public CredentialsDialog(string? initialUsername)
        : this()
    {
        // El nombre de usuario no es secreto y puede precargarse desde SQLite.
        if (!string.IsNullOrWhiteSpace(initialUsername))
        {
            UsernameBox.Text = initialUsername;
            UsernameBox.SelectAll();
        }
    }

    private void OnAccept(object sender, RoutedEventArgs e)
    {
        // username recibe el valor visible del TextBox para la operación actual.
        var username = UsernameBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(username))
        {
            MessageBox.Show(
                "Debe indicar un usuario.",
                "Camera Inspector — Credenciales",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            UsernameBox.Focus();
            return;
        }

        // PasswordBox.Password se copia únicamente al aceptar el diálogo.
        var password = PasswordBox.Password;

        if (string.IsNullOrEmpty(password))
        {
            MessageBox.Show(
                "Debe indicar una contraseña.",
                "Camera Inspector — Credenciales",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            PasswordBox.Focus();
            return;
        }

        Username = username;
        Password = password;
        SaveCredential = SaveCredentialCheckBox.IsChecked == true;
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        // No conservamos datos introducidos parcialmente cuando el técnico cancela.
        Username = string.Empty;
        Password = string.Empty;
        SaveCredential = false;
        DialogResult = false;
    }
}
