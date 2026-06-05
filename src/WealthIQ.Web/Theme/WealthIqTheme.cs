using MudBlazor;

namespace WealthIQ.Web.Theme;

/// <summary>
/// Central "Midnight Ledger" MudBlazor theme: emerald accent on a deep navy-slate base
/// (dark, primary) with a crisp paper-white light mode. Drives every MudBlazor component.
/// </summary>
public static class WealthIqTheme
{
    public static readonly MudTheme Instance = new()
    {
        PaletteLight = new PaletteLight
        {
            Primary = "#059669",
            Secondary = "#A78BFA",
            Info = "#2563EB",
            Success = "#059669",
            Warning = "#D97706",
            Error = "#DC2626",
            Background = "#F7F9FC",
            Surface = "#FFFFFF",
            AppbarBackground = "#FFFFFF",
            AppbarText = "#1E2733",
            DrawerBackground = "#FFFFFF",
            DrawerText = "#1E2733",
            TextPrimary = "#1E2733",
            TextSecondary = "#5A6678",
            ActionDefault = "#5A6678",
            LinesDefault = "#E6EAF0",
            TableLines = "#E6EAF0",
            DrawerIcon = "#5A6678",
        },
        PaletteDark = new PaletteDark
        {
            Primary = "#10B981",
            Secondary = "#A78BFA",
            Info = "#60A5FA",
            Success = "#34D399",
            Warning = "#FBBF24",
            Error = "#F87171",
            Black = "#0A0E17",
            Background = "#0F1420",
            Surface = "#161D2C",
            AppbarBackground = "#0A0E17",
            AppbarText = "#E7ECF3",
            DrawerBackground = "#0A0E17",
            DrawerText = "#B8C2D4",
            TextPrimary = "#E7ECF3",
            TextSecondary = "#7D8AA3",
            ActionDefault = "#7D8AA3",
            LinesDefault = "#232D40",
            TableLines = "#232D40",
            DrawerIcon = "#7D8AA3",
        },
        Typography = new Typography
        {
            Default = new DefaultTypography
            {
                FontFamily = new[] { "Inter", "system-ui", "-apple-system", "Segoe UI", "sans-serif" },
            },
        },
        LayoutProperties = new LayoutProperties
        {
            DefaultBorderRadius = "12px",
            DrawerWidthLeft = "248px",
        },
    };
}
