using System.Drawing;
using Forms = System.Windows.Forms;

namespace BuildMonitor.TrayApp.Services;

public static class TrayMenuTheme
{
    public static void Apply(Forms.ContextMenuStrip menu, ResolvedTheme theme)
    {
        var palette = theme == ResolvedTheme.Dark ? DarkPalette : LightPalette;

        menu.BackColor = palette.Background;
        menu.ForeColor = palette.Foreground;
        menu.Renderer = new ThemedMenuRenderer(palette);

        ApplyToItems(menu.Items, palette);
    }

    private static void ApplyToItems(Forms.ToolStripItemCollection items, MenuPalette palette)
    {
        foreach (Forms.ToolStripItem item in items)
        {
            item.BackColor = palette.Background;
            item.ForeColor = palette.Foreground;

            if (item is Forms.ToolStripMenuItem menuItem && menuItem.HasDropDownItems)
            {
                menuItem.DropDown.BackColor = palette.Background;
                menuItem.DropDown.ForeColor = palette.Foreground;
                ApplyToItems(menuItem.DropDownItems, palette);
            }
        }
    }

    private static MenuPalette DarkPalette => new(
        Background: Color.FromArgb(45, 45, 48),
        Foreground: Color.FromArgb(230, 230, 230),
        Highlight: Color.FromArgb(62, 122, 180),
        HighlightText: Color.White,
        Border: Color.FromArgb(70, 70, 74));

    private static MenuPalette LightPalette => new(
        Background: Color.FromArgb(245, 245, 245),
        Foreground: Color.FromArgb(30, 30, 30),
        Highlight: Color.FromArgb(0, 102, 204),
        HighlightText: Color.White,
        Border: Color.FromArgb(204, 204, 204));

    private sealed record MenuPalette(
        Color Background,
        Color Foreground,
        Color Highlight,
        Color HighlightText,
        Color Border);

    private sealed class ThemedMenuRenderer : Forms.ToolStripProfessionalRenderer
    {
        public ThemedMenuRenderer(MenuPalette palette)
            : base(new ThemedColorTable(palette))
        {
        }
    }

    private sealed class ThemedColorTable : Forms.ProfessionalColorTable
    {
        private readonly MenuPalette palette;

        public ThemedColorTable(MenuPalette palette) => this.palette = palette;

        public override Color MenuItemSelected => palette.Highlight;
        public override Color MenuItemSelectedGradientBegin => palette.Highlight;
        public override Color MenuItemSelectedGradientEnd => palette.Highlight;
        public override Color MenuItemBorder => palette.Border;
        public override Color MenuBorder => palette.Border;
        public override Color ToolStripDropDownBackground => palette.Background;
        public override Color ImageMarginGradientBegin => palette.Background;
        public override Color ImageMarginGradientMiddle => palette.Background;
        public override Color ImageMarginGradientEnd => palette.Background;
        public override Color SeparatorDark => palette.Border;
        public override Color SeparatorLight => palette.Border;
    }
}
