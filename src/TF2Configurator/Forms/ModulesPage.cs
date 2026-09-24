using System.Text;
using TF2Configurator.Models;
using TF2Configurator.Services;

namespace TF2Configurator.Forms;

/// <summary>
/// Editor for <c>tf\cfg\overrides\modules.cfg</c>. The file is round-tripped, not regenerated:
/// comments, blanks and entries this app doesn't know are left where they were. Levels that no
/// longer exist are flagged in the issues panel with a suggested replacement and only changed
/// when the user clicks — silently rewriting a config people tuned is worse than telling them
/// what's stale.
/// </summary>
internal sealed class ModulesPage : ConfigPage
{
    private readonly ToolTip _tip = UiKit.NewToolTip();

    private readonly Panel _scrollHost = new()
    {
        Dock = DockStyle.Fill,
        AutoScroll = true,
        BackColor = Color.White,
    };

    private readonly ListBox _categories = new()
    {
        Dock = DockStyle.Fill,
        BorderStyle = BorderStyle.None,
        IntegralHeight = false,
        ItemHeight = 22,
    };

    private readonly TextBox _search = new() { Width = 180 };
    private readonly CheckBox _onlySet = new() { Text = "Only modules set in file", AutoSize = true };

    private readonly ComboBox _preset = new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
        FlatStyle = FlatStyle.System,
        Width = 196,
    };

    private readonly ListView _issues = new()
    {
        Dock = DockStyle.Fill,
        View = View.Details,
        FullRowSelect = true,
        HideSelection = false,
        MultiSelect = false,
        BorderStyle = BorderStyle.None,
    };

    private readonly Panel _issuePanel = new()
    {
        Dock = DockStyle.Bottom,
        Height = 186,
        Visible = false,
        BackColor = UiKit.WarnBg,
    };

    private readonly Label _issueHeader = new()
    {
        Dock = DockStyle.Top,
        Height = 30,
        Font = UiKit.BoldFont,
        ForeColor = UiKit.WarnText,
        Padding = new Padding(10, 8, 0, 0),
    };

    private readonly Button _applyFix = UiKit.Btn("Apply suggested fix", 160);
    private readonly Button _removeLine = UiKit.Btn("Remove this line", 140);

    private readonly List<ModuleRow> _rows = new();
    private readonly List<Control> _ordered = new();
    private readonly Dictionary<string, Panel> _headers = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _pendingRemovals = new(StringComparer.OrdinalIgnoreCase);

    private string _rawOnLoad = "";
    private string? _selectedCategory;
    private bool _built;

    public ModulesPage(AppContextServices services) : base(services)
    {
        Controls.Add(_scrollHost);
        Controls.Add(BuildIssuePanel());
        Controls.Add(BuildCategoryPane());
        Controls.Add(BuildToolbar());

        _search.TextChanged += (_, _) => ApplyFilter();
        _onlySet.CheckedChanged += (_, _) => ApplyFilter();
        _categories.SelectedIndexChanged += OnCategoryChanged;
        _preset.SelectedIndexChanged += (_, _) => OnPresetPicked();
    }

    public override string Title => "Modules";

    // ---------------------------------------------------------------------------------------
    // Layout
    // ---------------------------------------------------------------------------------------

    private Control BuildToolbar()
    {
        var bar = new Panel
        {
            Dock = DockStyle.Top,
            Height = 46,
            BackColor = Color.White,
            Padding = new Padding(12, 0, 12, 0),
        };

        var searchLabel = new Label
        {
            Text = "Search:",
            AutoSize = true,
            Location = new Point(12, 15),
        };

        _search.Location = new Point(66, 11);
        _onlySet.Location = new Point(262, 14);

        var presetLabel = new Label
        {
            Text = "Preset:",
            AutoSize = true,
            Location = new Point(452, 15),
            Font = UiKit.BoldFont,
        };

        _preset.Location = new Point(510, 11);
        _preset.Items.Add("(apply a preset…)");
        foreach (var preset in PresetCatalog.PresetNames)
            _preset.Items.Add(Naming.Prettify(preset));

        // Right-to-left flow means the first control added ends up rightmost.
        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            WrapContents = false,
            Padding = new Padding(0, 9, 0, 0),
        };

        var save = UiKit.Btn("Save modules.cfg", 150);
        save.Font = UiKit.BoldFont;
        save.Click += (_, _) => Save();

        var reload = UiKit.Btn("Reload", 80);
        reload.Click += (_, _) => ReloadWithPrompt();

        var preview = UiKit.Btn("Preview file…", 110);
        preview.Click += (_, _) => PreviewFile();

        var whatChanged = UiKit.Btn("What changed…", 130);
        whatChanged.Click += (_, _) => ShowChanges();

        actions.Controls.Add(save);
        actions.Controls.Add(reload);
        actions.Controls.Add(preview);
        actions.Controls.Add(whatChanged);

        bar.Controls.Add(searchLabel);
        bar.Controls.Add(_search);
        bar.Controls.Add(_onlySet);
        bar.Controls.Add(presetLabel);
        bar.Controls.Add(_preset);
        bar.Controls.Add(actions);
        bar.Controls.Add(UiKit.Rule());

        _tip.SetToolTip(_search, "Filters by module name, display name, or description.");
        _tip.SetToolTip(_onlySet, "Show only modules that currently have a value in modules.cfg.");
        _tip.SetToolTip(_preset,
            "Sets every module to what this preset chooses, like comfig.app.\r\n" +
            "Only the pickers change — nothing is written until you save, so review before saving.\r\n" +
            "Modules a preset does not mention are left alone.");
        _tip.SetToolTip(preview, "See the exact file text a save would produce, before saving.");
        _tip.SetToolTip(whatChanged, "Shows only the lines that would change, compared with the file on disk.");
        _tip.SetToolTip(save, "Writes modules.cfg. The previous version is backed up first. (Ctrl+S)");

        return bar;
    }

    private Control BuildCategoryPane()
    {
        var pane = new Panel
        {
            Dock = DockStyle.Left,
            Width = 208,
            BackColor = Color.White,
        };

        pane.Controls.Add(_categories);
        pane.Controls.Add(new Panel { Dock = DockStyle.Right, Width = 1, BackColor = UiKit.Divider });
        return pane;
    }

    private Control BuildIssuePanel()
    {
        _issues.Columns.Add("Problem", 620);
        _issues.Columns.Add("Status", 300);
        _issues.SelectedIndexChanged += (_, _) => UpdateIssueButtons();

        var buttons = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 40,
            BackColor = UiKit.WarnBg,
            Padding = new Padding(10, 6, 10, 6),
        };

        _applyFix.Click += (_, _) => ApplySelectedFix();
        _removeLine.Click += (_, _) => RemoveSelectedLine();

        var flow = new FlowLayoutPanel
        {
            Dock = DockStyle.Left,
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            WrapContents = false,
        };
        flow.Controls.Add(_applyFix);
        flow.Controls.Add(_removeLine);

        var reassurance = new Label
        {
            Dock = DockStyle.Fill,
            Text = "Nothing here has been changed — these are suggestions only.",
            ForeColor = UiKit.WarnText,
            Font = UiKit.SmallFont,
            TextAlign = ContentAlignment.MiddleRight,
        };

        buttons.Controls.Add(flow);
        buttons.Controls.Add(reassurance);

        _issuePanel.Controls.Add(_issues);
        _issuePanel.Controls.Add(buttons);
        _issuePanel.Controls.Add(_issueHeader);
        _issuePanel.Controls.Add(new Panel { Dock = DockStyle.Top, Height = 1, BackColor = UiKit.Divider });

        _tip.SetToolTip(_applyFix, "Sets the module to the suggested current level. Still needs saving.");
        _tip.SetToolTip(_removeLine, "Drops the line on save, so the preset default applies again.");

        return _issuePanel;
    }

    // ---------------------------------------------------------------------------------------
    // Load
    // ---------------------------------------------------------------------------------------

    protected override void ReloadCore()
    {
        if (!Services.Paths.IsValid)
        {
            ShowMissingPath();
            return;
        }

        _pendingRemovals.Clear();
        _rawOnLoad = SafeWriter.Read(Services.Paths.ModulesCfg);
        var cfg = KeyValueCfg.Parse(_rawOnLoad);

        BuildRows();
        BuildCategoryList();

        foreach (var row in _rows)
            row.SetValue(cfg.Get(row.Module.Name), asBaseline: true);

        RefreshIssues(cfg);
        ApplyFilter();

        var exists = File.Exists(Services.Paths.ModulesCfg);
        SetStatus(exists
            ? $"Loaded {Services.Paths.ModulesCfg} — {cfg.Entries.Count()} entries."
            : $"No modules.cfg yet. Saving will create {Services.Paths.ModulesCfg}.");
    }

    private void ShowMissingPath()
    {
        _scrollHost.Controls.Clear();
        _scrollHost.Controls.Add(MissingPathNotice());
        _categories.Items.Clear();
        _issuePanel.Visible = false;
        _rows.Clear();
        _ordered.Clear();
        _headers.Clear();
        _built = false;
    }

    /// <summary>
    /// Creates one row per module, in catalog order, with a header before each category.
    /// Rebuilt on reload because a catalog refresh can add or remove modules.
    /// </summary>
    private void BuildRows()
    {
        _scrollHost.SuspendLayout();
        _scrollHost.Controls.Clear();
        _rows.Clear();
        _ordered.Clear();
        _headers.Clear();

        foreach (var category in Services.Catalog.Categories)
        {
            var header = UiKit.SectionHeader(
                category.Label,
                $"{category.Modules.Count} modules");

            _headers[category.Key] = header;
            _ordered.Add(header);

            var alternate = false;
            foreach (var module in category.Modules)
            {
                var row = new ModuleRow(module, alternate, _tip);
                row.ValueEdited += OnRowEdited;
                row.DetailsRequested += (_, _) => ShowModuleDetails(module);
                _rows.Add(row);
                _ordered.Add(row);
                alternate = !alternate;
            }
        }

        // Dock=Top stacks by descending z-order, so add in reverse to get catalog order.
        for (var i = _ordered.Count - 1; i >= 0; i--)
            _scrollHost.Controls.Add(_ordered[i]);

        _scrollHost.ResumeLayout();
        _built = true;
    }

    private void BuildCategoryList()
    {
        var previous = _selectedCategory;

        _categories.BeginUpdate();
        _categories.Items.Clear();
        _categories.Items.Add(new CategoryItem(null, $"All modules  ({Services.Catalog.ModuleCount})"));

        foreach (var category in Services.Catalog.Categories)
            _categories.Items.Add(new CategoryItem(category.Key, $"{category.Label}  ({category.Modules.Count})"));

        var index = 0;
        for (var i = 0; i < _categories.Items.Count; i++)
        {
            if (_categories.Items[i] is CategoryItem item &&
                string.Equals(item.Key, previous, StringComparison.OrdinalIgnoreCase))
            {
                index = i;
                break;
            }
        }

        _categories.SelectedIndex = index;
        _categories.EndUpdate();
        _selectedCategory = (_categories.SelectedItem as CategoryItem)?.Key;
    }

    // ---------------------------------------------------------------------------------------
    // Filtering
    // ---------------------------------------------------------------------------------------

    private void OnCategoryChanged(object? sender, EventArgs e)
    {
        _selectedCategory = (_categories.SelectedItem as CategoryItem)?.Key;
        ApplyFilter();
        _scrollHost.AutoScrollPosition = Point.Empty;
    }

    private void ApplyFilter()
    {
        if (!_built) return;

        var search = _search.Text.Trim();
        var onlySet = _onlySet.Checked;
        var visibleByCategory = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var shown = 0;

        _scrollHost.SuspendLayout();

        foreach (var row in _rows)
        {
            var inCategory = _selectedCategory is null ||
                             string.Equals(row.Module.CategoryKey, _selectedCategory,
                                 StringComparison.OrdinalIgnoreCase);

            var visible = inCategory && row.Matches(search) &&
                          (!onlySet || row.SelectedValue is not null);

            row.Visible = visible;
            if (!visible) continue;

            shown++;
            var key = row.Module.CategoryKey ?? "";
            visibleByCategory[key] = visibleByCategory.GetValueOrDefault(key) + 1;
        }

        foreach (var (key, header) in _headers)
            header.Visible = visibleByCategory.GetValueOrDefault(key) > 0;

        _scrollHost.ResumeLayout();

        var set = _rows.Count(r => r.SelectedValue is not null);
        SetStatus($"Showing {shown} of {_rows.Count} modules  ·  {set} set in modules.cfg" +
                  (_pendingRemovals.Count > 0 ? $"  ·  {_pendingRemovals.Count} line(s) queued for removal" : ""));
    }

    private void OnRowEdited(object? sender, EventArgs e)
    {
        MarkDirty();
        if (_onlySet.Checked) ApplyFilter();
    }

    // ---------------------------------------------------------------------------------------
    // Preset picker
    // ---------------------------------------------------------------------------------------

    private void OnPresetPicked()
    {
        if (_preset.SelectedIndex <= 0) return; // placeholder row
        var preset = PresetCatalog.PresetNames[_preset.SelectedIndex - 1];
        ApplyPreset(preset);

        // Put the placeholder back so the next click starts a fresh choice.
        _preset.SelectedIndex = 0;
    }

    /// <summary>
    /// Applies a preset to the pickers. Nothing is written to disk — the changes are normal
    /// unsaved edits, so the user reviews and saves like any other edit.
    /// </summary>
    private void ApplyPreset(string preset)
    {
        var changes = Services.Presets.EntriesFor(preset)
            .Where(e => FindRow(e.Module) is not null)
            .ToList();
        var sets = changes.Count(e => e.Value.Length > 0);
        var clears = changes.Count - sets;

        if (changes.Count == 0)
        {
            SetStatus($"The '{preset}' preset does not change any module here.");
            return;
        }

        var warning = "";
        if (Services.Paths.IsValid)
        {
            var status = Services.Paths.GetMastercomfigStatus();
            if (status.Presets.Count == 0)
                warning = "\r\n\r\nNote: no mastercomfig preset VPK is installed in tf\\custom, " +
                          "so preset defaults will not apply in-game. Consider installing one " +
                          "alongside mastercomfig.";
        }

        var answer = MessageBox.Show(this,
            $"Apply the '{preset}' preset?\r\n\r\n" +
            $"{changes.Count} modules will change — {sets} set to the preset's level, " +
            $"{clears} cleared so the preset default applies again.\r\n\r\n" +
            "Only the pickers change. Nothing is written to disk until you save, so you can " +
            "review every change first." +
            warning,
            "Apply preset", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);

        if (answer != DialogResult.OK) return;

        foreach (var (module, value) in changes)
        {
            var row = FindRow(module);
            if (row is null) continue;
            row.ApplyChange(value.Length == 0 ? null : value);
        }

        if (_onlySet.Checked) ApplyFilter();
        SetStatus($"Preset '{preset}' applied to {changes.Count} modules — not saved yet (Ctrl+S to save).");
    }

    // ---------------------------------------------------------------------------------------
    // Module details
    // ---------------------------------------------------------------------------------------

    private void ShowModuleDetails(ModuleDef module)
    {
        var current = FindRow(module.Name)?.SelectedValue;
        var sb = new StringBuilder();
        sb.Append(module.Label).Append("  (").Append(module.Name).AppendLine(")");
        if (!string.IsNullOrWhiteSpace(module.Description))
            sb.AppendLine().Append(module.Description);
        var cost = module.CostText;
        if (cost.Length > 0) sb.AppendLine().Append(cost);

        var sections = Services.Presets.DetailsFor(module.Name).ToList();
        if (sections.Count == 0)
        {
            sb.AppendLine().AppendLine()
              .Append("No per-level console variable data is bundled for this module.");
        }
        else
        {
            foreach (var section in sections)
            {
                sb.AppendLine().AppendLine()
                  .Append("Per-level console variables — ").AppendLine(section.Section);
                foreach (var (level, cvars) in section.Levels)
                {
                    var marker = string.Equals(level, current, StringComparison.OrdinalIgnoreCase)
                        ? "   (current)"
                        : "";
                    sb.AppendLine().Append("  ").Append(level).AppendLine(marker);
                    foreach (var (cvar, value) in cvars)
                        sb.Append("      ").Append(cvar).Append(" = ").AppendLine(value);
                }
            }
        }

        using var dialog = new TextDialog("Module details — " + module.Name, sb.ToString(),
            readOnly: true, note: "From mastercomfig's module data — informational only.");
        dialog.ShowDialog(this);
    }

    // ---------------------------------------------------------------------------------------
    // Issues
    // ---------------------------------------------------------------------------------------

    private void RefreshIssues(KeyValueCfg cfg)
    {
        var issues = ConfigValidator.Validate(cfg, Services.Catalog);

        _issues.BeginUpdate();
        _issues.Items.Clear();

        foreach (var issue in issues)
        {
            var item = new ListViewItem(issue.Description) { Tag = issue };
            item.SubItems.Add(issue.Resolution);
            item.ForeColor = issue.SuggestedValue is not null ? UiKit.WarnText : UiKit.Muted;
            _issues.Items.Add(item);
        }

        _issues.EndUpdate();

        _issuePanel.Visible = issues.Count > 0;
        _issueHeader.Text = issues.Count switch
        {
            0 => "",
            1 => "1 issue found in modules.cfg — the file was not modified",
            _ => $"{issues.Count} issues found in modules.cfg — the file was not modified",
        };

        UpdateIssueButtons();
    }

    private ConfigIssue? SelectedIssue =>
        _issues.SelectedItems.Count > 0 ? _issues.SelectedItems[0].Tag as ConfigIssue : null;

    private void UpdateIssueButtons()
    {
        var issue = SelectedIssue;
        _applyFix.Enabled = issue?.SuggestedValue is not null && FindRow(issue.Key) is not null;
        _removeLine.Enabled = issue is not null;
    }

    private ModuleRow? FindRow(string moduleName) =>
        _rows.FirstOrDefault(r =>
            string.Equals(r.Module.Name, moduleName, StringComparison.OrdinalIgnoreCase));

    private void ApplySelectedFix()
    {
        var issue = SelectedIssue;
        if (issue?.SuggestedValue is null) return;

        var row = FindRow(issue.Key);
        if (row is null) return;

        row.ApplyChange(issue.SuggestedValue);
        _pendingRemovals.Remove(issue.Key);

        // Re-validate against the current working copy so the issue list reflects reality now,
        // not just what was on disk when the page loaded.
        RefreshIssues(KeyValueCfg.Parse(BuildContents()));

        // Jump to the module so the change is visible rather than taken on trust.
        RevealRow(row);
    }

    private void RemoveSelectedLine()
    {
        var issue = SelectedIssue;
        if (issue is null) return;

        _pendingRemovals.Add(issue.Key);
        FindRow(issue.Key)?.ApplyChange(null);
        MarkDirty();
        RefreshIssues(KeyValueCfg.Parse(BuildContents()));
        ApplyFilter();
    }

    private void RevealRow(ModuleRow row)
    {
        var item = _categories.Items.Cast<object>()
            .OfType<CategoryItem>()
            .FirstOrDefault(c => string.Equals(c.Key, row.Module.CategoryKey,
                StringComparison.OrdinalIgnoreCase));

        if (item is not null && _selectedCategory is not null) _categories.SelectedItem = item;

        _search.Text = "";
        _onlySet.Checked = false;
        ApplyFilter();
        if (row.Visible) _scrollHost.ScrollControlIntoView(row);
    }

    // ---------------------------------------------------------------------------------------
    // Save
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Builds the file to write: the loaded text with the picker values and queued removals
    /// applied. Starting from the original text is what keeps comments, ordering, blanks and
    /// unknown keys.
    /// </summary>
    private string BuildContents()
    {
        var cfg = KeyValueCfg.Parse(_rawOnLoad);

        foreach (var row in _rows)
        {
            var value = row.SelectedValue;
            if (value is null) cfg.Remove(row.Module.Name);
            else cfg.Set(row.Module.Name, value);
        }

        foreach (var key in _pendingRemovals)
            cfg.Remove(key);

        return cfg.Render();
    }

    private void PreviewFile()
    {
        if (!Services.Paths.IsValid) return;
        TextDialog.Preview(this, "modules.cfg preview", BuildContents());
    }

    private void ShowChanges()
    {
        if (!Services.Paths.IsValid) return;
        TextDialog.ShowDiff(this, "Changes to modules.cfg",
            SafeWriter.Read(Services.Paths.ModulesCfg), BuildContents());
    }

    protected override bool SaveCore()
    {
        if (!Services.Paths.IsValid)
        {
            MessageBox.Show(this, "Set your Team Fortress 2 folder first.", "Cannot save",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        var contents = BuildContents();

        try
        {
            var backup = SafeWriter.Write(Services.Paths.ModulesCfg, contents);
            ReportSaved(Services.Paths.ModulesCfg, backup);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                "Could not write modules.cfg.\r\n\r\n" + ex.Message +
                "\r\n\r\nYour existing file is untouched.",
                "Save failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }

        // Re-baseline against what is now on disk so change highlighting stays accurate.
        _rawOnLoad = contents;
        _pendingRemovals.Clear();
        var cfg = KeyValueCfg.Parse(_rawOnLoad);
        foreach (var row in _rows)
            row.SetValue(cfg.Get(row.Module.Name), asBaseline: true);

        RefreshIssues(cfg);
        ApplyFilter();
        return true;
    }

    private void ReloadWithPrompt()
    {
        if (IsDirty && MessageBox.Show(this, "Discard unsaved changes to modules.cfg?", "Reload",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            return;

        Reload();
    }

    private sealed record CategoryItem(string? Key, string Text)
    {
        public override string ToString() => Text;
    }
}
