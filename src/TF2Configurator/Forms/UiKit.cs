namespace TF2Configurator.Forms;

/// <summary>Shared colours, fonts and small control factories, so pages look consistent.</summary>
internal static class UiKit
{
    public static readonly Color Muted = Color.FromArgb(105, 105, 112);
    public static readonly Color Accent = Color.FromArgb(0, 90, 158);
    public static readonly Color RowAlt = Color.FromArgb(248, 248, 251);
    public static readonly Color DirtyBg = Color.FromArgb(226, 240, 252);
    public static readonly Color WarnBg = Color.FromArgb(255, 248, 222);
    public static readonly Color WarnText = Color.FromArgb(140, 88, 0);
    public static readonly Color GoodText = Color.FromArgb(20, 120, 60);
    public static readonly Color BadText = Color.FromArgb(170, 40, 40);
    public static readonly Color PanelBg = Color.FromArgb(243, 243, 246);
    public static readonly Color Divider = Color.FromArgb(222, 222, 228);

    public static readonly Font BoldFont = new("Segoe UI", 9F, FontStyle.Bold);
    public static readonly Font SmallFont = new("Segoe UI", 8F);
    public static readonly Font SmallBoldFont = new("Segoe UI", 8F, FontStyle.Bold);
    public static readonly Font TitleFont = new("Segoe UI", 12F, FontStyle.Bold);
    public static readonly Font MonoFont = new("Consolas", 9.75F);

    public static Label Caption(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Font = BoldFont,
    };

    public static Label Note(string text) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = Muted,
        Font = SmallFont,
    };

    public static Button Btn(string text, int width = 110) => new()
    {
        Text = text,
        Width = width,
        Height = 27,
        UseVisualStyleBackColor = true,
        FlatStyle = FlatStyle.System,
    };

    /// <summary>A thin horizontal rule, for separating stacked sections.</summary>
    public static Panel Rule() => new()
    {
        Height = 1,
        Dock = DockStyle.Top,
        BackColor = Divider,
        Margin = Padding.Empty,
    };

    /// <summary>A section heading row for use inside a top-docked stack.</summary>
    public static Panel SectionHeader(string text, string? subtitle = null)
    {
        var panel = new Panel
        {
            Dock = DockStyle.Top,
            Height = subtitle is null ? 34 : 50,
            BackColor = PanelBg,
            Padding = new Padding(10, 0, 0, 0),
        };

        var title = new Label
        {
            Text = text,
            Font = BoldFont,
            AutoSize = true,
            Location = new Point(10, 9),
            ForeColor = Color.FromArgb(40, 40, 48),
        };
        panel.Controls.Add(title);

        if (subtitle is not null)
        {
            panel.Controls.Add(new Label
            {
                Text = subtitle,
                Font = SmallFont,
                ForeColor = Muted,
                AutoSize = true,
                Location = new Point(12, 28),
            });
        }

        return panel;
    }

    /// <summary>Attaches a tooltip to a control and every child it already contains.</summary>
    public static void SetTipDeep(ToolTip tip, Control control, string text)
    {
        tip.SetToolTip(control, text);
        foreach (Control child in control.Controls) SetTipDeep(tip, child, text);
    }

    public static ToolTip NewToolTip() => new()
    {
        AutoPopDelay = 30000,
        InitialDelay = 350,
        ReshowDelay = 100,
        ShowAlways = true,
    };
}
