using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using CameraInspector.App.ViewModels;
using CameraInspector.Core.Models;

namespace CameraInspector.App;

/// <summary>
/// Mejora la experiencia del diagnóstico y retira temporalmente la gestión de credenciales
/// de la ventana principal. Las credenciales siguen disponibles para las operaciones autenticadas.
/// </summary>
public partial class MainWindow
{
    private static readonly bool _diagnosticsUiHook = RegisterDiagnosticsUiHook();
    private bool _diagnosticsUiReady;

    private static bool RegisterDiagnosticsUiHook()
    {
        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnDiagnosticsUiLoaded));
        return true;
    }

    private static void OnDiagnosticsUiLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not MainWindow window)
            return;

        window.ConfigureDiagnosticsPanel();
        window.HideCredentialsFromMainWindow();
    }

    private void HideCredentialsFromMainWindow()
    {
        var tab = FindTabByHeader(this, "CREDENCIALES");
        if (tab is not null)
            tab.Visibility = Visibility.Collapsed;

        foreach (var button in FindVisualChildren<Button>(this))
        {
            var text = button.Content?.ToString() ?? string.Empty;
            if (text.Contains("GUARDAR CREDENCIALES", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("ELIMINAR CREDENCIALES", StringComparison.OrdinalIgnoreCase))
                button.Visibility = Visibility.Collapsed;
        }
    }

    private void ConfigureDiagnosticsPanel()
    {
        if (_diagnosticsUiReady)
            return;

        var tab = FindTabByHeader(this, "DIAGNÓSTICO");
        if (tab?.Content is not Grid grid)
            return;

        var resultGrid = grid.Children.OfType<DataGrid>().FirstOrDefault();
        if (resultGrid is null)
            return;

        _diagnosticsUiReady = true;
        resultGrid.AutoGenerateColumns = false;
        resultGrid.Columns.Clear();
        resultGrid.GridLinesVisibility = DataGridGridLinesVisibility.Horizontal;
        resultGrid.AlternatingRowBackground = (Brush)Application.Current.FindResource("Panel2Brush");
        resultGrid.Columns.Add(new DataGridTextColumn { Header = "PRUEBA", Binding = new Binding(nameof(DiagnosticResult.TestName)), Width = 130 });
        resultGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "RESULTADO",
            Binding = new Binding(nameof(DiagnosticResult.Success)) { Converter = new ResultTextConverter() },
            Width = 95
        });
        // ETAPA 1 (plan de diagnóstico): columna de severidad, para que de un vistazo se
        // distinga "bloqueante" (Crítico) de "revisar cuando se pueda" (Advertencia) en
        // vez de un simple OK/ALERTA binario.
        resultGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "SEVERIDAD",
            Binding = new Binding(nameof(DiagnosticResult.Severity)) { Converter = new SeverityTextConverter() },
            Width = 100
        });
        resultGrid.Columns.Add(new DataGridTextColumn { Header = "TIEMPO", Binding = new Binding(nameof(DiagnosticResult.Duration)) { StringFormat = "{0:mm\\:ss\\.fff}" }, Width = 90 });
        resultGrid.Columns.Add(new DataGridTextColumn { Header = "DETALLE", Binding = new Binding(nameof(DiagnosticResult.Message)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        // Columna nueva: qué hacer, en lenguaje de service, no el mensaje técnico crudo.
        resultGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "RECOMENDACIÓN",
            Binding = new Binding(nameof(DiagnosticResult.RecommendedAction)) { TargetNullValue = "—" },
            Width = new DataGridLength(1.2, DataGridLengthUnitType.Star)
        });

        resultGrid.LoadingRow += (_, args) =>
        {
            if (args.Row.DataContext is not DiagnosticResult result)
                return;
            args.Row.Foreground = result.NotSupported
                ? (Brush)Application.Current.FindResource("TextDimBrush")
                : result.Severity switch
                {
                    DiagnosticSeverity.Critico => (Brush)Application.Current.FindResource("ErrBrush"),
                    DiagnosticSeverity.Advertencia => (Brush)Application.Current.FindResource("WarnBrush"),
                    _ => result.Success
                        ? (Brush)Application.Current.FindResource("AccentBrush")
                        : (Brush)Application.Current.FindResource("TextDimBrush")
                };
        };

        var runButton = grid.Children.OfType<Button>().FirstOrDefault();
        if (runButton is not null)
        {
            runButton.Command = null;
            runButton.Content = "⚙ EJECUTAR BATERÍA";
            runButton.Width = 175;
            runButton.Click += async (_, _) =>
            {
                if (DataContext is MainViewModel vm)
                    await vm.RunQuickDiagnosticsCommand.ExecuteAsync(null);
            };
        }

        var controls = new StackPanel { Orientation = Orientation.Horizontal };
        if (runButton is not null)
        {
            grid.Children.Remove(runButton);
            controls.Children.Add(runButton);
        }

        var healthButton = new Button
        {
            Content = "↻ VERIFICAR SALUD",
            Width = 160,
            Height = 32,
            Margin = new Thickness(7, 0, 0, 0),
            Style = FindResource("PrimaryButton") as Style
        };
        healthButton.Click += async (_, _) =>
        {
            if (DataContext is not MainViewModel vm || vm.SelectedDevice is null)
            {
                SetStatus("ALERTA: seleccione una cámara antes de verificar la salud.");
                return;
            }
            try
            {
                SetStatus($"Verificando comunicación y vídeo de {vm.SelectedDevice.IpAddress}...");
                await vm.RecheckSelectedHealthAsync();
                SetStatus(vm.SelectedDevice.AlertDisplay);
            }
            catch (Exception ex)
            {
                SetStatus($"ALERTA: error verificando salud: {ex.Message}");
            }
        };
        controls.Children.Add(healthButton);

        var cancelButton = new Button { Content = "■ DETENER", Width = 115, Height = 32, Margin = new Thickness(7, 0, 0, 0) };
        cancelButton.Click += (_, _) =>
        {
            if (DataContext is MainViewModel vm)
                vm.CancelDiagnosticsCommand.Execute(null);
        };
        controls.Children.Add(cancelButton);
        Grid.SetRow(controls, 0);
        grid.Children.Add(controls);

        var footer = grid.Children.OfType<TextBlock>().FirstOrDefault();
        if (footer is not null)
        {
            // ETAPA 1 (plan de diagnóstico): antes era un texto fijo; ahora muestra el
            // resumen real "X/Y pruebas correctas" con desglose por severidad, actualizado
            // por MainViewModel tras cada corrida (ver DiagnosticsSummaryText).
            footer.SetBinding(TextBlock.TextProperty, new Binding(nameof(MainViewModel.DiagnosticsSummaryText)));
            footer.Foreground = (Brush)Application.Current.FindResource("TextDimBrush");
        }

        // ETAPA 2 (plan de diagnóstico): panel de conclusiones de causa raíz, debajo del
        // resumen. Se agrega como una fila nueva al final del Grid existente, así que no
        // hace falta reindexar ninguna de las filas ya definidas en el XAML.
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var conclusionsWrapper = new Border
        {
            Margin = new Thickness(0, 9, 0, 0),
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0)
        };
        conclusionsWrapper.SetBinding(
            UIElement.VisibilityProperty,
            new Binding(nameof(MainViewModel.DiagnosticConclusions) + ".Count") { Converter = new CountToVisibilityConverter() });
        Grid.SetRow(conclusionsWrapper, grid.RowDefinitions.Count - 1);

        var conclusionsPanel = new StackPanel();
        conclusionsPanel.Children.Add(new TextBlock
        {
            Text = "ANÁLISIS (qué significa esto y qué hacer)",
            FontFamily = new FontFamily("Consolas"),
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)Application.Current.FindResource("AccentBrush"),
            Margin = new Thickness(0, 0, 0, 6)
        });

        var conclusionsList = new ItemsControl();
        conclusionsList.SetBinding(ItemsControl.ItemsSourceProperty, new Binding(nameof(MainViewModel.DiagnosticConclusions)));
        conclusionsList.ItemTemplate = BuildConclusionCardTemplate();
        conclusionsPanel.Children.Add(conclusionsList);

        conclusionsWrapper.Child = conclusionsPanel;
        grid.Children.Add(conclusionsWrapper);
    }

    /// <summary>
    /// Construye, en código, la tarjeta de cada conclusión (título + explicación +
    /// recomendación opcional, coloreada por severidad). Se arma con FrameworkElementFactory
    /// en vez de XamlReader.Parse para evitar cualquier riesgo de parseo de XAML en tiempo
    /// de ejecución.
    /// </summary>
    private static DataTemplate BuildConclusionCardTemplate()
    {
        var template = new DataTemplate(typeof(DiagnosticConclusion));

        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 0, 6));
        border.SetValue(Border.PaddingProperty, new Thickness(10, 8, 10, 8));
        border.SetResourceReference(Border.BackgroundProperty, "Panel2Brush");
        border.SetValue(Border.BorderThicknessProperty, new Thickness(0, 0, 0, 3));
        border.SetBinding(Border.BorderBrushProperty, new Binding(nameof(DiagnosticConclusion.Severity)) { Converter = new SeverityBrushConverter() });

        var stack = new FrameworkElementFactory(typeof(StackPanel));
        border.AppendChild(stack);

        var title = new FrameworkElementFactory(typeof(TextBlock));
        title.SetBinding(TextBlock.TextProperty, new Binding(nameof(DiagnosticConclusion.Title)));
        title.SetValue(TextBlock.FontWeightProperty, FontWeights.Bold);
        title.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
        title.SetBinding(TextBlock.ForegroundProperty, new Binding(nameof(DiagnosticConclusion.Severity)) { Converter = new SeverityBrushConverter() });
        stack.AppendChild(title);

        var explanation = new FrameworkElementFactory(typeof(TextBlock));
        explanation.SetBinding(TextBlock.TextProperty, new Binding(nameof(DiagnosticConclusion.Explanation)));
        explanation.SetValue(TextBlock.MarginProperty, new Thickness(0, 4, 0, 0));
        explanation.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
        explanation.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        stack.AppendChild(explanation);

        var recommendation = new FrameworkElementFactory(typeof(TextBlock));
        recommendation.Name = "RecommendationText";
        recommendation.SetBinding(TextBlock.TextProperty, new Binding(nameof(DiagnosticConclusion.RecommendedAction)) { StringFormat = "→ {0}" });
        recommendation.SetValue(TextBlock.MarginProperty, new Thickness(0, 4, 0, 0));
        recommendation.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
        recommendation.SetValue(TextBlock.FontStyleProperty, FontStyles.Italic);
        recommendation.SetResourceReference(TextBlock.ForegroundProperty, "CommentBrush");
        stack.AppendChild(recommendation);

        template.VisualTree = border;

        var hideRecommendationWhenNull = new DataTrigger
        {
            Binding = new Binding(nameof(DiagnosticConclusion.RecommendedAction)),
            Value = null
        };
        hideRecommendationWhenNull.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Collapsed) { TargetName = "RecommendationText" });
        template.Triggers.Add(hideRecommendationWhenNull);

        return template;
    }

    private void SetStatus(string value)
    {
        if (DataContext is MainViewModel vm)
            vm.StatusText = value;
    }

    private static TabItem? FindTabByHeader(DependencyObject root, string header)
    {
        foreach (var tab in FindVisualChildren<TabItem>(root))
            if (string.Equals(tab.Header?.ToString(), header, StringComparison.OrdinalIgnoreCase))
                return tab;
        return null;
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root) where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T typed)
                yield return typed;
            foreach (var nested in FindVisualChildren<T>(child))
                yield return nested;
        }
    }

    private sealed class ResultTextConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            => value is bool ok && ok ? "OK" : "ALERTA";

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            => Binding.DoNothing;
    }

    private sealed class SeverityTextConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            => value is DiagnosticSeverity severity
                ? severity switch
                {
                    DiagnosticSeverity.Critico => "CRÍTICO",
                    DiagnosticSeverity.Advertencia => "ADVERTENCIA",
                    _ => "INFO"
                }
                : "INFO";

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            => Binding.DoNothing;
    }

    private sealed class SeverityBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            => Application.Current.FindResource(value is DiagnosticSeverity severity
                ? severity switch
                {
                    DiagnosticSeverity.Critico => "ErrBrush",
                    DiagnosticSeverity.Advertencia => "WarnBrush",
                    _ => "AccentBrush"
                }
                : "AccentBrush");

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            => Binding.DoNothing;
    }

    private sealed class CountToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            => value is int count && count > 0 ? Visibility.Visible : Visibility.Collapsed;

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            => Binding.DoNothing;
    }
}
