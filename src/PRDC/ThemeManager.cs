using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace PRDC;

internal enum AppTheme
{
    System,
    Light,
    Dark
}

internal static class ThemeManager
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 = 19;

    private static readonly Lazy<string> SettingsFilePath = new(() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PRDC",
            "settings.json"));

    private static readonly DarkToolStripRenderer DarkRendererInstance = new();
    private static readonly ToolStripProfessionalRenderer LightRendererInstance = new();

    public static event Action? ThemeChanged;

    public static AppTheme CurrentTheme { get; private set; } = AppTheme.System;

    public static bool IsDark => CurrentTheme switch
    {
        AppTheme.Dark => true,
        AppTheme.Light => false,
        _ => IsWindowsDarkTheme()
    };

    public static ToolStripRenderer DarkRenderer => DarkRendererInstance;
    public static ToolStripRenderer LightRenderer => LightRendererInstance;

    public static Color WindowBackground => IsDark ? Color.FromArgb(30, 30, 30) : SystemColors.Window;
    public static Color SurfaceBackground => IsDark ? Color.FromArgb(37, 37, 38) : SystemColors.Control;
    public static Color ControlBackground => IsDark ? Color.FromArgb(45, 45, 48) : SystemColors.Window;
    public static Color TextPrimary => IsDark ? Color.FromArgb(241, 241, 241) : SystemColors.ControlText;
    public static Color TextMuted => IsDark ? Color.FromArgb(160, 160, 160) : SystemColors.GrayText;
    public static Color BorderColor => IsDark ? Color.FromArgb(63, 63, 70) : SystemColors.ControlDark;
    public static Color AccentColor => Color.FromArgb(0, 122, 204);

    public static void Initialize()
    {
        try
        {
            string path = SettingsFilePath.Value;
            if (File.Exists(path))
            {
                string json = File.ReadAllText(path, Encoding.UTF8);
                using JsonDocument doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("theme", out JsonElement prop))
                {
                    string? themeStr = prop.GetString();
                    if (Enum.TryParse<AppTheme>(themeStr, ignoreCase: true, out AppTheme parsed))
                    {
                        CurrentTheme = parsed;
                    }
                }
            }
        }
        catch
        {
            CurrentTheme = AppTheme.System;
        }
    }

    public static void SetTheme(AppTheme theme)
    {
        if (CurrentTheme == theme)
        {
            return;
        }

        CurrentTheme = theme;
        SaveSettings();
        ThemeChanged?.Invoke();
    }

    public static void NotifySystemThemeChanged()
    {
        if (CurrentTheme == AppTheme.System)
        {
            ThemeChanged?.Invoke();
        }
    }

    private static void SaveSettings()
    {
        try
        {
            string path = SettingsFilePath.Value;
            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            Dictionary<string, object> dict = new(StringComparer.OrdinalIgnoreCase);
            if (File.Exists(path))
            {
                try
                {
                    string existingJson = File.ReadAllText(path, Encoding.UTF8);
                    using JsonDocument doc = JsonDocument.Parse(existingJson);
                    foreach (JsonProperty prop in doc.RootElement.EnumerateObject())
                    {
                        if (!string.Equals(prop.Name, "theme", StringComparison.OrdinalIgnoreCase))
                        {
                            dict[prop.Name] = prop.Value.Clone();
                        }
                    }
                }
                catch
                {
                    // Recria dicionário se o JSON existente for inválido
                }
            }

            dict["theme"] = CurrentTheme.ToString();

            string json = JsonSerializer.Serialize(dict, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json, new UTF8Encoding(false));
        }
        catch
        {
            // Salva silenciosamente sem bloquear o usuário
        }
    }

    public static bool IsWindowsDarkTheme()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            if (key?.GetValue("AppsUseLightTheme") is int useLight)
            {
                return useLight == 0;
            }
        }
        catch
        {
            // Fallback seguro se o registro não puder ser lido
        }

        return false;
    }

    public static void ApplyWindowChrome(Form form)
    {
        if (!form.IsHandleCreated)
        {
            form.HandleCreated += (s, _) =>
            {
                if (s is Form f && f.IsHandleCreated)
                {
                    ApplyWindowChromeInternal(f);
                }
            };
            return;
        }

        ApplyWindowChromeInternal(form);
    }

    private static void ApplyWindowChromeInternal(Form form)
    {
        try
        {
            int useDark = IsDark ? 1 : 0;
            int hr = DwmSetWindowAttribute(form.Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDark, sizeof(int));
            if (hr != 0)
            {
                DwmSetWindowAttribute(form.Handle, DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1, ref useDark, sizeof(int));
            }
        }
        catch
        {
            // Falha não impeditiva em versões antigas do Windows
        }
    }

    public static void ApplyTheme(Control root)
    {
        bool dark = IsDark;
        Color windowBg = WindowBackground;
        Color surfaceBg = SurfaceBackground;
        Color controlBg = ControlBackground;
        Color textPrimary = TextPrimary;
        Color textMuted = TextMuted;
        Color border = BorderColor;

        if (root is Form form)
        {
            form.BackColor = surfaceBg;
            form.ForeColor = textPrimary;
            ApplyWindowChrome(form);
        }

        ApplyToControlRecursive(root, dark, windowBg, surfaceBg, controlBg, textPrimary, textMuted, border);
    }

    private static void ApplyToControlRecursive(
        Control control,
        bool dark,
        Color windowBg,
        Color surfaceBg,
        Color controlBg,
        Color textPrimary,
        Color textMuted,
        Color border)
    {
        // Protege e preserva controles nativos RDP e WebView2 Guacamole
        if (control is AxHost || control.GetType().Name.Contains("WebView2", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (control.ContextMenuStrip is not null)
        {
            control.ContextMenuStrip.Renderer = dark ? DarkRendererInstance : LightRendererInstance;
            control.ContextMenuStrip.BackColor = surfaceBg;
            control.ContextMenuStrip.ForeColor = textPrimary;
            foreach (ToolStripItem item in control.ContextMenuStrip.Items)
            {
                item.ForeColor = textPrimary;
            }
        }

        switch (control)
        {
            case TreeView tree:
                tree.BackColor = windowBg;
                tree.ForeColor = textPrimary;
                tree.LineColor = dark ? Color.FromArgb(80, 80, 80) : SystemColors.GrayText;
                break;

            case MenuStrip menu:
                menu.Renderer = dark ? DarkRendererInstance : LightRendererInstance;
                menu.BackColor = dark ? Color.FromArgb(30, 30, 30) : SystemColors.Control;
                menu.ForeColor = textPrimary;
                foreach (ToolStripItem item in menu.Items)
                {
                    item.ForeColor = textPrimary;
                }
                break;

            case StatusStrip status:
                status.Renderer = dark ? DarkRendererInstance : LightRendererInstance;
                status.BackColor = surfaceBg;
                status.ForeColor = textPrimary;
                foreach (ToolStripItem item in status.Items)
                {
                    item.ForeColor = textPrimary;
                }
                break;

            case ContextMenuStrip contextMenu:
                contextMenu.Renderer = dark ? DarkRendererInstance : LightRendererInstance;
                contextMenu.BackColor = surfaceBg;
                contextMenu.ForeColor = textPrimary;
                foreach (ToolStripItem item in contextMenu.Items)
                {
                    item.ForeColor = textPrimary;
                }
                break;

            case TabControl tab:
                tab.DrawMode = TabDrawMode.OwnerDrawFixed;
                foreach (TabPage page in tab.TabPages)
                {
                    page.BackColor = windowBg;
                    page.ForeColor = textPrimary;
                    ApplyToControlRecursive(page, dark, windowBg, surfaceBg, controlBg, textPrimary, textMuted, border);
                }
                break;

            case TextBox textBox:
                textBox.BackColor = controlBg;
                textBox.ForeColor = textPrimary;
                textBox.BorderStyle = dark ? BorderStyle.FixedSingle : BorderStyle.Fixed3D;
                break;

            case Button button:
                if (dark)
                {
                    button.FlatStyle = FlatStyle.Flat;
                    button.BackColor = controlBg;
                    button.ForeColor = textPrimary;
                    button.FlatAppearance.BorderColor = border;
                }
                else
                {
                    if (button.Dock == DockStyle.Fill)
                    {
                        button.FlatStyle = FlatStyle.Flat;
                        button.BackColor = surfaceBg;
                        button.ForeColor = textPrimary;
                        button.FlatAppearance.BorderColor = border;
                    }
                    else
                    {
                        button.FlatStyle = FlatStyle.Standard;
                        button.UseVisualStyleBackColor = true;
                        button.ForeColor = SystemColors.ControlText;
                    }
                }
                break;

            case LinkLabel link:
                link.LinkColor = dark ? Color.FromArgb(86, 156, 214) : Color.Blue;
                link.VisitedLinkColor = dark ? Color.FromArgb(170, 120, 200) : Color.Purple;
                link.ActiveLinkColor = dark ? Color.FromArgb(120, 190, 255) : Color.Red;
                break;

            case Label label:
                label.ForeColor = textPrimary;
                break;

            case SplitContainer split:
                split.BackColor = surfaceBg;
                split.Panel1.BackColor = surfaceBg;
                split.Panel2.BackColor = surfaceBg;
                break;

            case Panel panel:
                panel.BackColor = surfaceBg;
                break;
        }

        foreach (Control child in control.Controls)
        {
            ApplyToControlRecursive(child, dark, windowBg, surfaceBg, controlBg, textPrimary, textMuted, border);
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    internal sealed class DarkColorTable : ProfessionalColorTable
    {
        public override Color MenuStripGradientBegin => Color.FromArgb(30, 30, 30);
        public override Color MenuStripGradientEnd => Color.FromArgb(30, 30, 30);
        public override Color ToolStripDropDownBackground => Color.FromArgb(37, 37, 38);
        public override Color ImageMarginGradientBegin => Color.FromArgb(37, 37, 38);
        public override Color ImageMarginGradientMiddle => Color.FromArgb(37, 37, 38);
        public override Color ImageMarginGradientEnd => Color.FromArgb(37, 37, 38);
        public override Color MenuBorder => Color.FromArgb(63, 63, 70);
        public override Color MenuItemBorder => Color.FromArgb(0, 122, 204);
        public override Color MenuItemSelected => Color.FromArgb(50, 50, 54);
        public override Color MenuItemSelectedGradientBegin => Color.FromArgb(50, 50, 54);
        public override Color MenuItemSelectedGradientEnd => Color.FromArgb(50, 50, 54);
        public override Color MenuItemPressedGradientBegin => Color.FromArgb(37, 37, 38);
        public override Color MenuItemPressedGradientMiddle => Color.FromArgb(37, 37, 38);
        public override Color MenuItemPressedGradientEnd => Color.FromArgb(37, 37, 38);
        public override Color CheckBackground => Color.FromArgb(50, 50, 54);
        public override Color CheckSelectedBackground => Color.FromArgb(70, 70, 76);
        public override Color CheckPressedBackground => Color.FromArgb(0, 122, 204);
        public override Color SeparatorDark => Color.FromArgb(63, 63, 70);
        public override Color SeparatorLight => Color.FromArgb(37, 37, 38);
        public override Color StatusStripGradientBegin => Color.FromArgb(37, 37, 38);
        public override Color StatusStripGradientEnd => Color.FromArgb(37, 37, 38);
        public override Color ButtonSelectedHighlight => Color.FromArgb(50, 50, 54);
        public override Color ButtonPressedHighlight => Color.FromArgb(0, 122, 204);
    }

    internal sealed class DarkToolStripRenderer : ToolStripProfessionalRenderer
    {
        public DarkToolStripRenderer() : base(new DarkColorTable())
        {
            RoundedEdges = false;
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = (e.Item?.Enabled ?? true)
                ? Color.FromArgb(241, 241, 241)
                : Color.FromArgb(120, 120, 120);
            base.OnRenderItemText(e);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = (e.Item?.Enabled ?? true)
                ? Color.FromArgb(241, 241, 241)
                : Color.FromArgb(120, 120, 120);
            base.OnRenderArrow(e);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            Rectangle checkBounds = new(e.ImageRectangle.Left - 2, e.ImageRectangle.Top - 2, e.ImageRectangle.Width + 4, e.ImageRectangle.Height + 4);
            using (SolidBrush boxBrush = new(Color.FromArgb(50, 50, 54)))
            using (Pen borderPen = new(Color.FromArgb(0, 122, 204)))
            {
                e.Graphics.FillRectangle(boxBrush, checkBounds);
                e.Graphics.DrawRectangle(borderPen, checkBounds);
            }

            using Pen checkPen = new(Color.FromArgb(241, 241, 241), 2);
            Point p1 = new(checkBounds.Left + 3, checkBounds.Top + (checkBounds.Height / 2));
            Point p2 = new(checkBounds.Left + (checkBounds.Width / 2) - 1, checkBounds.Bottom - 4);
            Point p3 = new(checkBounds.Right - 3, checkBounds.Top + 4);
            e.Graphics.DrawLine(checkPen, p1, p2);
            e.Graphics.DrawLine(checkPen, p2, p3);
        }
    }
}
