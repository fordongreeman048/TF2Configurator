using TF2Configurator.Models;

namespace TF2Configurator.Forms;

/// <summary>
/// One module in the editor: name, CPU/GPU cost, description and a level picker. Two things keep
/// it safe: "(not set)" genuinely removes the key (so the preset default applies again — not the
/// same as picking the lowest level), and a level the current catalog doesn't know is kept in the
/// list and stays selected, so saving can't quietly drop it.
/// </summary>
internal sealed class ModuleRow : Panel
{
    private const int ComboWidth = 250;
    private const string UnsetText = "(not set — use preset default)";

    private readonly ComboBox _combo = new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
        FlatStyle = FlatStyle.System,
    };

    private readonly Button _details = UiKit.Btn("Details…", 74);

    private readonly Label _desc = new()
    {
        AutoSize = false,
        ForeColor = UiKit.Muted,
        Font = UiKit.SmallFont,
    };

    private readonly Color _baseColor;
    private string? _original;
    private bool _suppress;

    public ModuleDef Module { get; }

    public event EventHandler? ValueEdited;

    /// <summary>Raised when the user asks to see the cvars behind this module's levels.</summary>
    public event EventHandler? DetailsRequested;

    public ModuleRow(ModuleDef module, bool alternate, ToolTip tip)
    {
        Module = module;
        _baseColor = alternate ? UiKit.RowAlt : Color.White;

        Dock = DockStyle.Top;
        Height = 64;
        BackColor = _baseColor;

        var nameLabel = new Label
        {
            Text = module.Label,
            Font = UiKit.BoldFont,
            AutoSize = true,
            Location = new Point(12, 9),
            ForeColor = Color.FromArgb(35, 35, 42),
        };

        var nameWidth = TextRenderer.MeasureText(module.Label, UiKit.BoldFont).Width;

        var keyLabel = new Label
        {
            Text = module.Name,
            Font = UiKit.SmallFont,
            AutoSize = true,
            ForeColor = UiKit.Muted,
            Location = new Point(12 + nameWidth + 10, 11),
        };

        var keyWidth = TextRenderer.MeasureText(module.Name, UiKit.SmallFont).Width;

        var costLabel = new Label
        {
            Text = module.CostText,
            Font = UiKit.SmallFont,
            AutoSize = true,
            ForeColor = UiKit.Accent,
            Location = new Point(12 + nameWidth + keyWidth + 22, 11),
        };

        _desc.Text = module.Description ?? "";

        _combo.Items.Add(new Choice(null, UnsetText));
        foreach (var value in module.Values)
            _combo.Items.Add(new Choice(value.Value, value.Label));

        _combo.SelectedIndexChanged += OnComboChanged;
        _details.Click += (_, _) => DetailsRequested?.Invoke(this, EventArgs.Empty);

        Controls.Add(nameLabel);
        Controls.Add(keyLabel);
        Controls.Add(costLabel);
        Controls.Add(_desc);
        Controls.Add(_combo);
        Controls.Add(_details);

        var tipText = module.Tooltip;
        tip.SetToolTip(this, tipText);
        tip.SetToolTip(nameLabel, tipText);
        tip.SetToolTip(keyLabel, tipText);
        tip.SetToolTip(costLabel, tipText);
        tip.SetToolTip(_desc, tipText);
        tip.SetToolTip(_combo, tipText);
        tip.SetToolTip(_details,
            "Shows the console variables each level sets, from mastercomfig's module data.");

        LayoutChildren();
    }

    /// <summary>The level to write, or null to leave the module unset.</summary>
    public string? SelectedValue => (_combo.SelectedItem as Choice)?.Value;

    /// <summary>True when the picker no longer matches what was read from disk.</summary>
    public bool IsChanged =>
        !string.Equals(SelectedValue ?? "", _original ?? "", StringComparison.OrdinalIgnoreCase);

    /// <summary>True when the file assigns this module a level the catalog does not define.</summary>
    public bool HasUnrecognisedValue { get; private set; }

    /// <summary>
    /// Sets the picker from the file. <paramref name="asBaseline"/> also clears the change
    /// marker (that's the load path); "apply suggestion" does not.
    /// </summary>
    public void SetValue(string? value, bool asBaseline)
    {
        _suppress = true;
        try
        {
            var index = IndexOf(value);
            if (index < 0 && !string.IsNullOrWhiteSpace(value))
            {
                // Unknown level: keep it visible and selectable rather than resetting the row.
                _combo.Items.Add(new Choice(value, $"{value}  —  not in current catalog"));
                index = _combo.Items.Count - 1;
                HasUnrecognisedValue = true;
            }

            _combo.SelectedIndex = index < 0 ? 0 : index;
            if (asBaseline)
            {
                _original = SelectedValue;
                HasUnrecognisedValue = index >= 0 && !Module.HasValue(SelectedValue ?? "") &&
                                       SelectedValue is not null;
            }
        }
        finally
        {
            _suppress = false;
        }

        Repaint();
    }

    /// <summary>
    /// Used by the issues panel to change the level like the user picked it — pass null to unset.
    /// Counts as an edit, so it still needs saving.
    /// </summary>
    public void ApplyChange(string? value)
    {
        SetValue(value, asBaseline: false);
        ValueEdited?.Invoke(this, EventArgs.Empty);
    }

    public bool Matches(string search)
    {
        if (search.Length == 0) return true;
        if (Module.Name.Contains(search, StringComparison.OrdinalIgnoreCase)) return true;
        if (Module.Label.Contains(search, StringComparison.OrdinalIgnoreCase)) return true;
        if (Module.Description?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false) return true;
        if (Module.Tags is { Count: > 0 } &&
            Module.Tags.Any(t => t.Contains(search, StringComparison.OrdinalIgnoreCase))) return true;
        if (Module.Notes is { Count: > 0 } &&
            Module.Notes.Any(n => n.Content?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false))
            return true;
        return false;
    }

    private int IndexOf(string? value)
    {
        for (var i = 0; i < _combo.Items.Count; i++)
        {
            if (_combo.Items[i] is Choice c &&
                string.Equals(c.Value ?? "", value ?? "", StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return -1;
    }

    private void OnComboChanged(object? sender, EventArgs e)
    {
        if (_suppress) return;
        Repaint();
        ValueEdited?.Invoke(this, EventArgs.Empty);
    }

    private void Repaint()
    {
        BackColor = IsChanged ? UiKit.DirtyBg
                  : HasUnrecognisedValue ? UiKit.WarnBg
                  : _baseColor;

        var selected = Module.Find(SelectedValue ?? "");
        _desc.Text = selected?.Detail is { Length: > 0 } detail
            ? detail
            : Module.Description ?? "";
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        LayoutChildren();
    }

    private void LayoutChildren()
    {
        _combo.SetBounds(Math.Max(20, Width - ComboWidth - 16), 17, ComboWidth, 24);
        _details.SetBounds(Math.Max(20, Width - ComboWidth - 16 - 8 - 74), 16, 74, 26);
        _desc.SetBounds(12, 30, Math.Max(60, Width - ComboWidth - 130), 30);
    }

    /// <summary>Combo entry pairing the level written to the file with its display text.</summary>
    private sealed record Choice(string? Value, string Text)
    {
        public override string ToString() => Text;
    }
}
