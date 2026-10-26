using System.Windows;
using System.Windows.Media;
using Wpf.Ui.Appearance;

namespace DockNest.Services;

public static class ThemeService
{
    public static void Apply(string theme)
    {
        bool light = theme == "Light";
        ApplicationThemeManager.Apply(light ? ApplicationTheme.Light : ApplicationTheme.Dark, Wpf.Ui.Controls.WindowBackdropType.None, false);
        string[] names = ["Canvas", "Sidebar", "Surface", "Hover", "Border", "Text", "Muted", "Subtle", "Accent", "AccentText", "AccentSoft", "Danger"];
        string[] darkColors = ["#0F141C", "#141B25", "#19222E", "#222E3D", "#283443", "#ECF1F7", "#9CAABB", "#6C7F94", "#86E1C6", "#102A25", "#203E3A", "#F59A9A"];
        string[] lightColors = ["#F6F8FB", "#EEF2F6", "#FFFFFF", "#E8EEF4", "#DCE3EC", "#1A2939", "#596D82", "#6B8096", "#187963", "#FFFFFF", "#D9EDE6", "#B83242"];
        for (var i = 0; i < names.Length; i++)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString((light ? lightColors : darkColors)[i]));
            brush.Freeze();
            Application.Current.Resources[names[i] + "Brush"] = brush;
        }
    }
}
