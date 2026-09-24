using TF2Configurator.Models;
using TF2Configurator.Services;

namespace TF2Configurator.Forms;

/// <summary>One console variable: label, current value, and where it came from.</summary>
internal sealed class CvarRow : Panel
{
    private const int EditorWidth = 250;
    private const string UnsetText = "(not set)";

    private readonly CvarDef _def;
    private readonly ComboBox? _combo;
    private readonly TextBox? _box;
    private readonly Label _desc;
    private readonly Color _baseColor;

    private string? _original;
    private bool _suppress;

    /// <summary>
    /// False until the child controls exist. Setting <see cref="Control.Height"/> in the
    /// constructor fires <see cref="OnSizeChanged"/> right away, which would otherwise lay out
    /// controls that don't exist yet.
    /// </summary>
    private bool _ready;

    public event EventHandler? ValueEdited;

    public CvarRow(CvarDef def, bool alternate, ToolTip tip)
    {
        _def = def;
        _baseColor = alternate ? UiKit.RowAlt : Color.White;

        Dock = DockStyle.Top;
        Height = 62;
        BackColor = _baseColor;

        var nameLabel = new Label
        {
            Text = def.Label,
            Font = UiKit.BoldFont,
            AutoSize = true,
            Location = new Point(12, 8),
            ForeColor = Color.FromArgb(35, 35, 42),
        };

        var x = 12 + TextRenderer.MeasureText(def.Label, UiKit.BoldFont).Width + 10;

        var keyLabel = new Label
        {
            Text = def.Name,
            Font = UiKit.SmallFont,
            AutoSize = true,
            ForeColor = UiKit.Muted,
            Location = new Point(x, 10),
        };

        Controls.Add(nameLabel);
        Controls.Add(keyLabel);

        if (def.ManagedByModule is not null)
        {
            x += TextRenderer.MeasureText(def.Name, UiKit.SmallFont).Width + 10;
            var badge = new Label
            {
                Text = $"⚠ overrides the '{def.ManagedByModule}' module",
                Font = UiKit.SmallFont,
                AutoSize = true,
                ForeColor = UiKit.WarnText,
                Location = new Point(x, 10),
            };
            Controls.Add(badge);
            tip.SetToolTip(badge, def.Tooltip);
        }

        _desc = new Label
        {
            Text = def.Description ?? "",
            AutoSize = false,
            ForeColor = UiKit.Muted,
            Font = UiKit.SmallFont,
        };
        Controls.Add(_desc);

        if (def.Kind == CvarKind.Choice && def.Choices is { Length: > 0 })
        {
            _combo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                FlatStyle = FlatStyle.System,
            };
            _combo.Items.Add(new Choice(null, UnsetText));
            foreach (var (value, label) in def.Choices)
                _combo.Items.Add(new Choice(value, label));
            _combo.SelectedIndexChanged += OnEdited;
            Controls.Add(_combo);
            tip.SetToolTip(_combo, def.Tooltip);
        }
        else
        {
            _box = new TextBox { PlaceholderText = UnsetText };
            _box.TextChanged += OnEdited;
            Controls.Add(_box);
            tip.SetToolTip(_box, def.Tooltip);
        }

        tip.SetToolTip(this, def.Tooltip);
        tip.SetToolTip(nameLabel, def.Tooltip);
        tip.SetToolTip(keyLabel, def.Tooltip);
        tip.SetToolTip(_desc, def.Tooltip);

        _ready = true;
        LayoutChildren();
    }

    public CvarDef Def => _def;

    /// <summary>The value to write, or null when the cvar should not appear in the file.</summary>
    public string? Value
    {
        get
        {
            if (_combo is not null) return (_combo.SelectedItem as Choice)?.Value;
            var text = _box!.Text.Trim();
            return text.Length == 0 ? null : text;
        }
    }

    public bool IsChanged => (Value ?? "") != (_original ?? "");

    /// <summary>True when the file sets this to something the picker does not offer.</summary>
    public bool HasUnrecognisedValue { get; private set; }

    public void SetValue(string? value, bool asBaseline)
    {
        _suppress = true;
        try
        {
            if (_combo is not null)
            {
                var index = IndexOf(value);
                if (index < 0 && !string.IsNullOrWhiteSpace(value))
                {
                    _combo.Items.Add(new Choice(value, $"{value}  —  custom value"));
                    index = _combo.Items.Count - 1;
                    HasUnrecognisedValue = true;
                }

                _combo.SelectedIndex = index < 0 ? 0 : index;
            }
            else
            {
                _box!.Text = value ?? "";
            }

            if (asBaseline) _original = Value;
        }
        finally
        {
            _suppress = false;
        }

        Repaint();
    }

    public bool Matches(string search) =>
        search.Length == 0
        || _def.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
        || _def.Label.Contains(search, StringComparison.OrdinalIgnoreCase)
        || (_def.Description?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false);

    private int IndexOf(string? value)
    {
        for (var i = 0; i < _combo!.Items.Count; i++)
        {
            if (_combo.Items[i] is Choice c &&
                string.Equals(c.Value ?? "", value ?? "", StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return -1;
    }

    private void OnEdited(object? sender, EventArgs e)
    {
        if (_suppress) return;
        Repaint();
        ValueEdited?.Invoke(this, EventArgs.Empty);
    }

    private void Repaint() =>
        BackColor = IsChanged ? UiKit.DirtyBg
                  : HasUnrecognisedValue ? UiKit.WarnBg
                  : _baseColor;

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        LayoutChildren();
    }

    private void LayoutChildren()
    {
        if (!_ready) return;

        var editor = (Control?)_combo ?? _box!;
        editor.SetBounds(Math.Max(20, Width - EditorWidth - 16), 16, EditorWidth, 24);
        _desc.SetBounds(12, 30, Math.Max(60, Width - EditorWidth - 40), 28);
    }

    private sealed record Choice(string? Value, string Text)
    {
        public override string ToString() => Text;
    }
}

/// <summary>
/// Scrollable, grouped list of <see cref="CvarRow"/>. Shared by the autoexec and per-class
/// editors, which only differ in the cvars they offer and the file they write.
/// </summary>
internal sealed class CvarEditor : Panel
{
    private readonly ToolTip _tip;
    private readonly List<CvarRow> _rows = new();
    private readonly List<Control> _ordered = new();
    private readonly Dictionary<string, Panel> _headers = new(StringComparer.Ordinal);

    public event EventHandler? ValueEdited;

    public CvarEditor(IEnumerable<CvarDef> defs, ToolTip tip)
    {
        _tip = tip;
        Dock = DockStyle.Fill;
        AutoScroll = true;
        BackColor = Color.White;

        foreach (var group in defs.GroupBy(d => d.Group))
        {
            var header = UiKit.SectionHeader(group.Key);
            _headers[group.Key] = header;
            _ordered.Add(header);

            var alternate = false;
            foreach (var def in group)
            {
                var row = new CvarRow(def, alternate, _tip);
                row.ValueEdited += (_, _) => ValueEdited?.Invoke(this, EventArgs.Empty);
                _rows.Add(row);
                _ordered.Add(row);
                alternate = !alternate;
            }
        }

        // Dock=Top stacks by descending z-order, so add in reverse to keep declared order.
        for (var i = _ordered.Count - 1; i >= 0; i--)
            Controls.Add(_ordered[i]);
    }

    public IReadOnlyList<CvarRow> Rows => _rows;

    public int SetCount => _rows.Count(r => r.Value is not null);

    /// <summary>Cvars that are set here but normally controlled by a mastercomfig module.</summary>
    public IEnumerable<CvarRow> ModuleConflicts =>
        _rows.Where(r => r.Value is not null && r.Def.ManagedByModule is not null);

    public void LoadFrom(CommandCfg cfg)
    {
        foreach (var row in _rows)
            row.SetValue(cfg.Get(row.Def.Name), asBaseline: true);
    }

    public void ApplyTo(CommandCfg cfg)
    {
        foreach (var row in _rows)
        {
            var value = row.Value;
            if (value is null) cfg.Remove(row.Def.Name);
            else cfg.Set(row.Def.Name, value);
        }
    }

    public void Filter(string search, bool onlySet)
    {
        var visibleByGroup = new Dictionary<string, int>(StringComparer.Ordinal);

        SuspendLayout();

        foreach (var row in _rows)
        {
            var visible = row.Matches(search) && (!onlySet || row.Value is not null);
            row.Visible = visible;
            if (visible)
                visibleByGroup[row.Def.Group] = visibleByGroup.GetValueOrDefault(row.Def.Group) + 1;
        }

        foreach (var (group, header) in _headers)
            header.Visible = visibleByGroup.GetValueOrDefault(group) > 0;

        ResumeLayout();
    }

    public int VisibleCount => _rows.Count(r => r.Visible);
    public int TotalCount => _rows.Count;
}
