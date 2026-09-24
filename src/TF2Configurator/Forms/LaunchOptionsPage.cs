using System.Diagnostics;
using TF2Configurator.Services;

namespace TF2Configurator.Forms;

/// <summary>
/// Builds a TF2 launch-option string. It deliberately never writes Steam's
/// <c>localconfig.vdf</c>: Steam keeps that file in memory and rewrites it on exit, so a behind-
/// its-back edit is usually thrown away — and sometimes corrupts the surrounding block. Text to
/// paste is the honest option, so the string is saved here and copied out by hand.
/// </summary>
internal sealed class LaunchOptionsPage : ConfigPage
{
    private readonly ToolTip _tip = UiKit.NewToolTip();
    private readonly Panel _scrollHost = new()
    {
        Dock = DockStyle.Fill,
        AutoScroll = true,
        BackColor = Color.White,
    };

    private readonly TextBox _output = new()
    {
        Dock = DockStyle.Fill,
        Multiline = true,
        ScrollBars = ScrollBars.Vertical,
        Font = UiKit.MonoFont,
        BackColor = Color.White,
    };

    private readonly Label _outputNote = new()
    {
        Dock = DockStyle.Bottom,
        Height = 22,
        Font = UiKit.SmallFont,
        ForeColor = UiKit.Muted,
        Padding = new Padding(2, 4, 0, 0),
    };

    private readonly List<OptionRow> _rows = new();
    private readonly List<Control> _ordered = new();

    /// <summary>Tokens the catalog does not know about, kept so editing never drops them.</summary>
    private readonly List<string> _extras = new();

    private string _baseline = "";
    private bool _syncing;

    public LaunchOptionsPage(AppContextServices services) : base(services)
    {
        BuildRows();

        Controls.Add(_scrollHost);
        Controls.Add(BuildOutputPanel());
        Controls.Add(BuildToolbar());
    }

    public override string Title => "Launch options";

    // ---------------------------------------------------------------------------------------
    // Layout
    // ---------------------------------------------------------------------------------------

    private void BuildRows()
    {
        foreach (var group in LaunchOptionCatalog.All.GroupBy(o => o.Group))
        {
            var recommended = group.All(o => o.Recommended);
            _ordered.Add(UiKit.SectionHeader(
                group.Key,
                recommended ? "safe for everyone — start here" : null));

            var alternate = false;
            foreach (var def in group)
            {
                var row = new OptionRow(def, alternate, _tip);
                row.Changed += (_, _) => OnRowChanged();
                _rows.Add(row);
                _ordered.Add(row);
                alternate = !alternate;
            }
        }

        // Dock=Top stacks by descending z-order, so add in reverse to keep catalog order.
        for (var i = _ordered.Count - 1; i >= 0; i--)
            _scrollHost.Controls.Add(_ordered[i]);
    }

    private Control BuildToolbar()
    {
        var bar = new Panel
        {
            Dock = DockStyle.Top,
            Height = 92,
            BackColor = UiKit.PanelBg,
            Padding = new Padding(12, 8, 12, 8),
        };

        var intro = new Label
        {
            AutoSize = false,
            Dock = DockStyle.Top,
            Height = 40,
            Font = UiKit.SmallFont,
            ForeColor = UiKit.Muted,
            Text =
                "Steam library → right-click Team Fortress 2 → Properties → General → Launch Options, " +
                "then paste the line below.\r\n" +
                "This app will not edit Steam's files directly: Steam rewrites them when it exits and would " +
                "discard the change.",
        };

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            WrapContents = false,
        };

        var recommended = UiKit.Btn("Apply recommended", 160);
        recommended.Font = UiKit.BoldFont;
        recommended.Click += (_, _) => ApplyRecommended();

        var clear = UiKit.Btn("Clear all", 90);
        clear.Click += (_, _) => ClearAll();

        var copy = UiKit.Btn("Copy to clipboard", 150);
        copy.Click += (_, _) => CopyToClipboard();

        var remember = UiKit.Btn("Save for later", 120);
        remember.Click += (_, _) => Save();

        var launch = UiKit.Btn("Launch TF2 once", 140);
        launch.Click += (_, _) => LaunchOnce();

        actions.Controls.Add(recommended);
        actions.Controls.Add(clear);
        actions.Controls.Add(copy);
        actions.Controls.Add(remember);
        actions.Controls.Add(launch);

        bar.Controls.Add(intro);
        bar.Controls.Add(actions);
        bar.Controls.Add(UiKit.Rule());

        _tip.SetToolTip(recommended,
            "Ticks the options that are safe on any machine and leaves everything else as it is.");
        _tip.SetToolTip(clear, "Unticks everything, including anything typed in the box below.");
        _tip.SetToolTip(copy, "Copies the line below, ready to paste into Steam.");
        _tip.SetToolTip(remember,
            "Stores this line in the app's settings so it is here next time. (Ctrl+S)\r\n" +
            "It does not change Steam — you still need to paste it.");
        _tip.SetToolTip(launch,
            "Asks Steam to launch TF2 once with these options, via steam://run/440.\r\n" +
            "The options apply to this launch only — Steam's saved launch options are not touched.");

        return bar;
    }

    private Control BuildOutputPanel()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 118,
            BackColor = Color.White,
            Padding = new Padding(12, 8, 12, 8),
        };

        var caption = UiKit.Caption("Launch options");
        caption.Dock = DockStyle.Top;
        caption.Height = 22;

        _output.TextChanged += (_, _) => OnOutputEdited();

        panel.Controls.Add(_output);
        panel.Controls.Add(_outputNote);
        panel.Controls.Add(caption);
        panel.Controls.Add(UiKit.Rule());

        _tip.SetToolTip(_output,
            "Editable — paste your current launch options here and the checkboxes above will follow.\r\n" +
            "Anything this app does not recognise is kept at the end untouched.");

        return panel;
    }

    // ---------------------------------------------------------------------------------------
    // Load / save
    // ---------------------------------------------------------------------------------------

    protected override void ReloadCore()
    {
        _baseline = Services.Settings.LaunchOptions ?? "";
        LoadFromText(_baseline);
        SetStatus(_baseline.Length == 0
            ? "No launch options saved yet. Tick what you want, or paste your current ones into the box."
            : "Loaded the launch options saved in this app. Paste them into Steam to apply.");
    }

    protected override bool SaveCore()
    {
        Services.Settings.LaunchOptions = _output.Text.Trim();
        Services.Settings.Save();
        _baseline = Services.Settings.LaunchOptions;

        SetStatus("Saved in this app — now paste it into Steam's launch options to take effect.");
        return true;
    }

    // ---------------------------------------------------------------------------------------
    // Two-way sync between the checkboxes and the text
    // ---------------------------------------------------------------------------------------

    private void OnRowChanged()
    {
        if (_syncing) return;
        RegenerateText();
        MarkDirtyIfChanged();
    }

    private void OnOutputEdited()
    {
        if (_syncing) return;
        LoadFromText(_output.Text, keepText: true);
        MarkDirtyIfChanged();
    }

    private void MarkDirtyIfChanged() => MarkDirty(_output.Text.Trim() != _baseline.Trim());

    /// <summary>
    /// Parses a launch-option string into the rows. Unknown tokens go to <see cref="_extras"/>
    /// instead of being dropped — someone's <c>-insecure</c> or a mod switch isn't ours to delete.
    /// </summary>
    private void LoadFromText(string text, bool keepText = false)
    {
        _syncing = true;
        try
        {
            var tokens = Tokenize(text);
            var seen = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            _extras.Clear();

            for (var i = 0; i < tokens.Count; i++)
            {
                var token = tokens[i];
                var def = LaunchOptionCatalog.All.FirstOrDefault(o =>
                    string.Equals(o.Switch, token, StringComparison.OrdinalIgnoreCase));

                if (def is null)
                {
                    _extras.Add(token);
                    continue;
                }

                string? value = null;
                if (def.Kind == LaunchOptionKind.Value &&
                    i + 1 < tokens.Count && !IsSwitch(tokens[i + 1]))
                {
                    value = tokens[i + 1];
                    i++;
                }

                seen[def.Switch] = value;
            }

            foreach (var row in _rows)
            {
                var on = seen.TryGetValue(row.Def.Switch, out var value);
                row.SetState(on, on ? value : null);
            }

            if (!keepText) RegenerateTextCore();
            UpdateNote();
        }
        finally
        {
            _syncing = false;
        }
    }

    private void RegenerateText()
    {
        _syncing = true;
        try
        {
            RegenerateTextCore();
            UpdateNote();
        }
        finally
        {
            _syncing = false;
        }
    }

    private void RegenerateTextCore()
    {
        var parts = new List<string>();

        foreach (var row in _rows.Where(r => r.IsOn))
        {
            parts.Add(row.Def.Switch);
            if (row.Def.Kind != LaunchOptionKind.Value) continue;

            var value = row.Value;
            if (!string.IsNullOrWhiteSpace(value))
                parts.Add(value.Contains(' ') ? $"\"{value}\"" : value);
        }

        parts.AddRange(_extras);
        _output.Text = string.Join(" ", parts);
    }

    private void UpdateNote()
    {
        var cautions = _rows.Where(r => r.IsOn && r.Def.Caution is not null).ToList();
        var on = _rows.Count(r => r.IsOn);

        if (cautions.Count > 0)
        {
            _outputNote.Text = $"⚠ {string.Join("   ·   ", cautions.Select(c => $"{c.Def.Switch}: {c.Def.Caution}"))}";
            _outputNote.ForeColor = UiKit.WarnText;
        }
        else if (_extras.Count > 0)
        {
            _outputNote.Text = $"{on} option(s) selected. Kept as-is because they are not in this app's list: " +
                               string.Join(" ", _extras);
            _outputNote.ForeColor = UiKit.Muted;
        }
        else
        {
            _outputNote.Text = $"{on} option(s) selected.";
            _outputNote.ForeColor = UiKit.Muted;
        }
    }

    // ---------------------------------------------------------------------------------------
    // Actions
    // ---------------------------------------------------------------------------------------

    private void ApplyRecommended()
    {
        _syncing = true;
        foreach (var row in _rows.Where(r => r.Def.Recommended))
            row.SetState(true, row.Def.DefaultValue);
        _syncing = false;

        RegenerateText();
        MarkDirtyIfChanged();
        SetStatus("Ticked the recommended options. Nothing else was changed.");
    }

    private void ClearAll()
    {
        if (MessageBox.Show(this,
                "Untick every option and clear the text box?", "Clear launch options",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;

        _syncing = true;
        foreach (var row in _rows) row.SetState(false, null);
        _extras.Clear();
        _syncing = false;

        RegenerateText();
        MarkDirtyIfChanged();
        SetStatus("Cleared. Nothing is saved until you press \"Save for later\".");
    }

    private void CopyToClipboard()
    {
        var text = _output.Text.Trim();
        if (text.Length == 0)
        {
            SetStatus("Nothing to copy — no options are selected.");
            return;
        }

        try
        {
            Clipboard.SetText(text);
            SetStatus("Copied. Paste it into Steam → TF2 → Properties → Launch Options.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                "Could not access the clipboard.\r\n\r\n" + ex.Message +
                "\r\n\r\nYou can select the text in the box and copy it manually.",
                "Copy failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    /// <summary>
    /// Launches TF2 once with the current options via Steam's run URI. Applies only to that
    /// launch — Steam's saved options are never touched, per the page's rule.
    /// </summary>
    private void LaunchOnce()
    {
        var text = _output.Text.Trim();
        if (text.Length == 0)
        {
            SetStatus("Nothing to launch with — no options are selected.");
            return;
        }

        if (MessageBox.Show(this,
                "Launch TF2 once with these launch options?\r\n\r\n  " + text +
                "\r\n\r\nSteam must be running. The options apply to this launch only — they do " +
                "not change Steam's saved launch options.",
                "Launch TF2 once", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
            return;

        try
        {
            var uri = "steam://run/440//" + Uri.EscapeDataString(text);
            Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
            SetStatus("Asked Steam to launch TF2 with those options.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                "Could not ask Steam to launch TF2.\r\n\r\n" + ex.Message +
                "\r\n\r\nYour options are untouched — you can still copy and paste them manually.",
                "Launch failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    // ---------------------------------------------------------------------------------------
    // Parsing helpers
    // ---------------------------------------------------------------------------------------

    private static bool IsSwitch(string token) =>
        token.StartsWith('-') || token.StartsWith('+');

    /// <summary>Splits on whitespace, keeping double-quoted runs together.</summary>
    private static List<string> Tokenize(string text)
    {
        var tokens = new List<string>();
        var current = new System.Text.StringBuilder();
        var quoted = false;

        foreach (var c in text)
        {
            if (c == '"')
            {
                quoted = !quoted;
                continue;
            }

            if (!quoted && char.IsWhiteSpace(c))
            {
                if (current.Length > 0)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                }

                continue;
            }

            current.Append(c);
        }

        if (current.Length > 0) tokens.Add(current.ToString());
        return tokens;
    }

    // ---------------------------------------------------------------------------------------
    // Row
    // ---------------------------------------------------------------------------------------

    private sealed class OptionRow : Panel
    {
        private const int ValueWidth = 110;

        private readonly CheckBox _check = new() { AutoSize = true, Location = new Point(12, 8) };
        private readonly TextBox? _value;
        private readonly Label _desc;
        private readonly Color _baseColor;

        /// <summary>See the note on <see cref="CvarRow"/>: Height fires a layout before the
        /// child controls exist.</summary>
        private bool _ready;

        public event EventHandler? Changed;

        public LaunchOptionDef Def { get; }

        public OptionRow(LaunchOptionDef def, bool alternate, ToolTip tip)
        {
            Def = def;
            _baseColor = alternate ? UiKit.RowAlt : Color.White;

            Dock = DockStyle.Top;
            Height = def.Caution is null ? 58 : 74;
            BackColor = _baseColor;

            _check.Text = def.Label;
            _check.Font = UiKit.BoldFont;
            _check.CheckedChanged += (_, _) =>
            {
                if (_value is not null) _value.Enabled = _check.Checked;
                Repaint();
                Changed?.Invoke(this, EventArgs.Empty);
            };

            var checkWidth = TextRenderer.MeasureText(def.Label, UiKit.BoldFont).Width;

            var switchLabel = new Label
            {
                Text = def.Switch,
                Font = UiKit.SmallFont,
                AutoSize = true,
                ForeColor = UiKit.Accent,
                Location = new Point(34 + checkWidth + 10, 10),
            };

            _desc = new Label
            {
                Text = def.Description ?? "",
                AutoSize = false,
                Font = UiKit.SmallFont,
                ForeColor = UiKit.Muted,
            };

            Controls.Add(_check);
            Controls.Add(switchLabel);
            Controls.Add(_desc);

            if (def.Kind == LaunchOptionKind.Value)
            {
                _value = new TextBox
                {
                    Text = def.DefaultValue ?? "",
                    Enabled = false,
                    PlaceholderText = "value",
                };
                _value.TextChanged += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
                Controls.Add(_value);
                tip.SetToolTip(_value, def.Description ?? def.Switch);
            }

            if (def.Caution is not null)
            {
                Controls.Add(new Label
                {
                    Text = "⚠ " + def.Caution,
                    AutoSize = false,
                    Font = UiKit.SmallFont,
                    ForeColor = UiKit.WarnText,
                    Bounds = new Rectangle(32, 46, 700, 18),
                });
            }

            var tipText = def.Description is null
                ? def.Switch
                : $"{def.Switch}\r\n\r\n{def.Description}" +
                  (def.Caution is null ? "" : $"\r\n\r\n⚠ {def.Caution}");

            tip.SetToolTip(this, tipText);
            tip.SetToolTip(_check, tipText);
            tip.SetToolTip(switchLabel, tipText);
            tip.SetToolTip(_desc, tipText);

            _ready = true;
            LayoutChildren();
        }

        public bool IsOn => _check.Checked;

        public string? Value => _value?.Text.Trim();

        public void SetState(bool on, string? value)
        {
            _check.Checked = on;
            if (_value is null) return;

            if (!string.IsNullOrWhiteSpace(value)) _value.Text = value;
            else if (on && _value.Text.Trim().Length == 0) _value.Text = Def.DefaultValue ?? "";

            _value.Enabled = on;
        }

        private void Repaint() => BackColor = _check.Checked ? UiKit.RowAlt : _baseColor;

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            LayoutChildren();
        }

        private void LayoutChildren()
        {
            if (!_ready) return;

            _value?.SetBounds(Math.Max(20, Width - ValueWidth - 16), 8, ValueWidth, 24);
            _desc.SetBounds(32, 28, Math.Max(60, Width - ValueWidth - 60), 18);
        }
    }
}
