using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CameraInspector.App;

/// <summary>
/// Ventana de diálogo con el tema oscuro de la app. Reemplaza a System.Windows.MessageBox,
/// cuya ventana es dibujada por Windows con el tema claro del sistema operativo sin
/// importar cómo esté configurado el resto de la aplicación — es la fuente más visible
/// de "algo en blanco" en una app pensada para verse toda oscura.
/// </summary>
public partial class ThemedMessageBoxWindow : Window
{
    public MessageBoxResult Result { get; private set; } = MessageBoxResult.None;

    public ThemedMessageBoxWindow(
        Window? owner,
        string messageBoxText,
        string caption,
        MessageBoxButton button,
        MessageBoxImage icon)
    {
        InitializeComponent();

        Title = caption;
        if (owner is not null)
        {
            Owner = owner;
        }
        else
        {
            // Sin owner explícito: se centra sobre la ventana activa de la app (mismo
            // criterio que ya se usa en NetworkConfigurationEditViewModel para diálogos
            // de credenciales), o en pantalla si no hay ninguna ventana activa todavía.
            var activeWindow = Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
                                ?? Application.Current?.MainWindow;
            if (activeWindow is not null)
                Owner = activeWindow;
            else
                WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        MessageTextBlock.Text = messageBoxText;
        ConfigureIcon(icon);
        ConfigureButtons(button);
    }

    private void ConfigureIcon(MessageBoxImage icon)
    {
        var (glyph, brushKey) = icon switch
        {
            MessageBoxImage.Error => ("✕", "ErrBrush"),         // == Hand == Stop
            MessageBoxImage.Warning => ("!", "WarnBrush"),      // == Exclamation
            MessageBoxImage.Question => ("?", "KeywordBrush"),
            MessageBoxImage.Information => ("i", "AccentBrush"),// == Asterisk
            _ => (string.Empty, "TextDimBrush")
        };

        if (string.IsNullOrEmpty(glyph))
        {
            IconBadge.Visibility = Visibility.Collapsed;
            return;
        }

        var brush = (Brush)FindResource(brushKey);
        IconGlyph.Text = glyph;
        IconGlyph.Foreground = brush;
        IconBadge.BorderBrush = brush;
    }

    private void ConfigureButtons(MessageBoxButton button)
    {
        switch (button)
        {
            case MessageBoxButton.OK:
                AddButton("ACEPTAR", MessageBoxResult.OK, isDefault: true, isCancel: true);
                break;
            case MessageBoxButton.OKCancel:
                AddButton("CANCELAR", MessageBoxResult.Cancel, isDefault: false, isCancel: true);
                AddButton("ACEPTAR", MessageBoxResult.OK, isDefault: true, isCancel: false);
                break;
            case MessageBoxButton.YesNoCancel:
                AddButton("CANCELAR", MessageBoxResult.Cancel, isDefault: false, isCancel: true);
                AddButton("NO", MessageBoxResult.No, isDefault: false, isCancel: false);
                AddButton("SÍ", MessageBoxResult.Yes, isDefault: true, isCancel: false);
                break;
            case MessageBoxButton.YesNo:
                AddButton("NO", MessageBoxResult.No, isDefault: false, isCancel: true);
                AddButton("SÍ", MessageBoxResult.Yes, isDefault: true, isCancel: false);
                break;
        }
    }

    private void AddButton(string text, MessageBoxResult result, bool isDefault, bool isCancel)
    {
        var button = new Button
        {
            Content = text,
            Margin = new Thickness(8, 0, 0, 0),
            MinWidth = 92,
            Style = (Style)FindResource(isDefault ? "PrimaryButton" : "SecondaryButton"),
            IsDefault = isDefault,
            IsCancel = isCancel
        };
        button.Click += (_, _) =>
        {
            Result = result;
            DialogResult = true;
            Close();
        };
        ButtonsPanel.Children.Add(button);
    }

    protected override void OnClosed(EventArgs e)
    {
        // Si se cerró con Alt+F4/Esc sin pasar por ningún botón, se comporta igual que
        // System.Windows.MessageBox: Cancel si existe esa opción, si no, None.
        if (Result == MessageBoxResult.None)
            Result = ButtonsPanel.Children.OfType<Button>().Any(b => b.IsCancel) ? MessageBoxResult.Cancel : MessageBoxResult.None;

        base.OnClosed(e);
    }
}

/// <summary>
/// Reemplazo directo (mismo nombre de método, misma firma) de System.Windows.MessageBox.Show
/// con el tema oscuro de la app. Se usa así en todo el proyecto en vez de MessageBox.Show.
/// </summary>
public static class ThemedMessageBox
{
    public static MessageBoxResult Show(string messageBoxText, string caption, MessageBoxButton button, MessageBoxImage icon)
        => ShowInternal(null, messageBoxText, caption, button, icon);

    public static MessageBoxResult Show(Window owner, string messageBoxText, string caption, MessageBoxButton button, MessageBoxImage icon)
        => ShowInternal(owner, messageBoxText, caption, button, icon);

    public static MessageBoxResult Show(string messageBoxText, string caption)
        => ShowInternal(null, messageBoxText, caption, MessageBoxButton.OK, MessageBoxImage.None);

    public static MessageBoxResult Show(string messageBoxText)
        => ShowInternal(null, messageBoxText, "Camera Inspector", MessageBoxButton.OK, MessageBoxImage.None);

    private static MessageBoxResult ShowInternal(
        Window? owner,
        string messageBoxText,
        string caption,
        MessageBoxButton button,
        MessageBoxImage icon)
    {
        var dialog = new ThemedMessageBoxWindow(owner, messageBoxText, caption, button, icon);
        dialog.ShowDialog();
        return dialog.Result;
    }
}
