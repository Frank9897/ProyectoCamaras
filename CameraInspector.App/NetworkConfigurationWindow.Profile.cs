using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CameraInspector.Core.Models;
using CameraInspector.Network.Configuration;

namespace CameraInspector.App;

/// <summary>
/// Agrega a la ventana de configuración una guía adaptada al fabricante detectado.
/// La guía nunca habilita una operación que el provider no haya implementado.
/// </summary>
public partial class NetworkConfigurationWindow
{
    private bool _profileUiConfigured;

    private void ConfigureManufacturerProfileUi()
    {
        if (_profileUiConfigured)
            return;

        var tabs = FindVisualChild<TabControl>(this);
        if (tabs is null)
            return;

        var profile = CameraConfigurationProfileResolver.Resolve(_viewModelDevice());
        _profileUiConfigured = true;

        Title = $"Camera Inspector — Configuración de cámara IP — {profile.Manufacturer}";

        var content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var stack = new StackPanel { Margin = new Thickness(18) };
        content.Content = stack;

        stack.Children.Add(new TextBlock
        {
            Text = "PERFIL DE ADMINISTRACIÓN",
            FontFamily = new FontFamily("Consolas"),
            FontSize = 15,
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)FindResource("AccentBrush")
        });
        stack.Children.Add(new TextBlock
        {
            Text = profile.ProfileName,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 17,
            Foreground = (Brush)FindResource("TextBrush"),
            Margin = new Thickness(0, 5, 0, 0)
        });

        var summary = new Border
        {
            Margin = new Thickness(0, 12, 0, 10),
            Padding = new Thickness(12),
            Background = (Brush)FindResource("Panel2Brush"),
            BorderBrush = (Brush)FindResource("BorderBrush2"),
            BorderThickness = new Thickness(1),
            Child = new StackPanel()
        };
        var summaryStack = (StackPanel)summary.Child;
        AddField(summaryStack, "HERRAMIENTA / REFERENCIA", profile.DiscoveryTool);
        AddField(summaryStack, "ESTILO", profile.ManagementStyle);
        AddField(summaryStack, "PROTOCOLO", profile.PrimaryProtocol);
        AddField(summaryStack, "ESTADO", profile.Manufacturer == "GENÉRICO" ? "GENÉRICO · NO SE IDENTIFICÓ FABRICANTE" : "FABRICANTE RECONOCIDO");
        stack.Children.Add(summary);

        stack.Children.Add(new TextBlock
        {
            Text = profile.Description,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)FindResource("TextDimBrush"),
            Margin = new Thickness(0, 0, 0, 12)
        });

        var capability = new Border
        {
            Padding = new Thickness(12),
            Background = (Brush)FindResource("Panel3Brush"),
            BorderBrush = (Brush)FindResource("BorderBrush2"),
            BorderThickness = new Thickness(1)
        };
        var capabilityStack = new StackPanel();
        capability.Child = capabilityStack;
        capabilityStack.Children.Add(new TextBlock
        {
            Text = "CAPACIDADES PREVISTAS",
            FontFamily = new FontFamily("Consolas"),
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)FindResource("AccentBrush")
        });
        AddCapability(capabilityStack, "DHCP", profile.SupportsDhcp);
        AddCapability(capabilityStack, "IPv4 FIJA", profile.SupportsStaticIpv4);
        AddCapability(capabilityStack, "GATEWAY", profile.SupportsGateway);
        AddCapability(capabilityStack, "DNS", profile.SupportsDns);
        AddCapability(capabilityStack, "HOSTNAME", profile.SupportsHostname);
        AddCapability(capabilityStack, "NTP", profile.SupportsNtp);
        AddCapability(capabilityStack, "PUERTOS / SERVICIOS", profile.SupportsPorts);
        AddCapability(capabilityStack, "CREDENCIALES", profile.SupportsCredentials);
        AddCapability(capabilityStack, "REINICIO", profile.SupportsReboot);
        AddCapability(capabilityStack, "FACTORY RESET", profile.SupportsFactoryReset);
        stack.Children.Add(capability);

        stack.Children.Add(new TextBlock
        {
            Text = "FLUJO RECOMENDADO",
            Margin = new Thickness(0, 16, 0, 7),
            FontFamily = new FontFamily("Consolas"),
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)FindResource("AccentBrush")
        });
        foreach (var action in profile.RecommendedActions)
        {
            stack.Children.Add(new TextBlock
            {
                Text = $"• {action}",
                Margin = new Thickness(0, 2, 0, 2),
                Foreground = (Brush)FindResource("TextBrush"),
                TextWrapping = TextWrapping.Wrap
            });
        }

        var note = new Border
        {
            Margin = new Thickness(0, 16, 0, 0),
            Padding = new Thickness(12),
            Background = (Brush)FindResource("Panel3Brush"),
            BorderBrush = profile.Manufacturer == "GENÉRICO"
                ? (Brush)FindResource("WarnBrush")
                : (Brush)FindResource("BorderBrush2"),
            BorderThickness = new Thickness(1),
            Child = new TextBlock
            {
                Text = profile.Manufacturer == "GENÉRICO"
                    ? "ALERTA: no se identificó un fabricante conocido. Camera Inspector no inventará menús ni comandos propietarios; utilizará capacidades detectadas y el flujo ONVIF/HTTP genérico."
                    : "NOTA: el perfil adapta la interfaz al ecosistema del fabricante, pero una capacidad específica puede variar según modelo y firmware. Las acciones no confirmadas deben mostrar ALERTA y no ejecutarse silenciosamente.",
                Foreground = profile.Manufacturer == "GENÉRICO"
                    ? (Brush)FindResource("WarnBrush")
                    : (Brush)FindResource("TextDimBrush"),
                TextWrapping = TextWrapping.Wrap
            }
        };
        stack.Children.Add(note);

        tabs.Items.Insert(0, new TabItem
        {
            Header = "PERFIL",
            Content = content
        });
        tabs.SelectedIndex = 0;
    }

    private DiscoveredDevice _viewModelDevice() => _viewModel.Device;

    private void AddField(Panel panel, string label, string value)
    {
        var row = new StackPanel { Margin = new Thickness(0, 0, 0, 7) };
        row.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 9,
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)FindResource("TextDimBrush")
        });
        row.Children.Add(new TextBlock
        {
            Text = value,
            FontFamily = new FontFamily("Consolas"),
            Foreground = (Brush)FindResource("TextBrush"),
            TextWrapping = TextWrapping.Wrap
        });
        panel.Children.Add(row);
    }

    private void AddCapability(Panel panel, string name, bool supported)
    {
        panel.Children.Add(new TextBlock
        {
            Text = $"{(supported ? "✓" : "—")} {name}",
            Foreground = supported
                ? (Brush)FindResource("AccentBrush")
                : (Brush)FindResource("TextDimBrush"),
            FontFamily = new FontFamily("Consolas"),
            Margin = new Thickness(0, 3, 0, 0)
        });
    }

    private static T? FindVisualChild<T>(DependencyObject root) where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T typed)
                return typed;

            var nested = FindVisualChild<T>(child);
            if (nested is not null)
                return nested;
        }

        return null;
    }
}
