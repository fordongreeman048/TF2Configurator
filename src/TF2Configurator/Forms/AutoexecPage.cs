using TF2Configurator.Models;
using TF2Configurator.Services;

namespace TF2Configurator.Forms;

/// <summary>
/// Editor for <c>tf\cfg\overrides\autoexec.cfg</c>. Same round-trip contract as the modules
/// page: the file on disk is the base, and only the cvars this page manages are rewritten.
/// Aliases, binds, exec lines and comments are left alone — autoexec is where people keep
/// hand-written scripts.
/// </summary>
internal sealed class AutoexecPage : ConfigPage
{
    private readonly ToolTip _tip = UiKit.NewToolTip();
    private readonly CvarEditor _editor;

    private readonly TextBox _search = new() { Width = 210 };
    private readonly CheckBox _onlySet = new() { Text = "Only settings in file", AutoSize = true };

    private readonly Panel _conflictPanel = new()
    {
        Dock = DockStyle.Bottom,
        Height = 80,
        Visible = false,
        BackColor = UiKit.WarnBg,
        Padding = new Padding(12, 8, 12, 8),
    };

    private readonly Label _conflictText = new()
    {
        Dock = DockStyle.Fill,
        ForeColor = UiKit.WarnText,
        Font = UiKit.SmallFont,
    };

    private readonly Panel _host = new() { Dock = DockStyle.Fill };

    private string _rawOnLoad = "";
    private bool _built;

    public AutoexecPage(AppContextServices services) : base(services)
    {
        _editor = new CvarEditor(CvarCatalog.Autoexec, _tip);
        _editor.ValueEdited += OnEdited;

        Controls.Add(_host);
        Controls.Add(BuildConflictPanel());
        Controls.Add(BuildToolbar());

        _search.TextChanged += (_, _) => ApplyFilter();
        _onlySet.CheckedChanged += (_, _) => ApplyFilter();
    }

    public override string Title => "autoexec.cfg";

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
        };

        var searchLabel = new Label { Text = "Search:", AutoSize = true, Location = new Point(12, 15) };
        _search.Location = new Point(66, 11);
        _onlySet.Location = new Point(290, 14);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            WrapContents = false,
            Padding = new Padding(0, 9, 12, 0),
        };

        var save = UiKit.Btn("Save autoexec.cfg", 150);
        save.Font = UiKit.BoldFont;
        save.Click += (_, _) => Save();

        var reload = UiKit.Btn("Reload", 80);
        reload.Click += (_, _) => ReloadWithPrompt();

        var preview = UiKit.Btn("Preview file…", 110);
        preview.Click += (_, _) => PreviewFile();

        var rawEdit = UiKit.Btn("Edit raw text…", 120);
        rawEdit.Click += (_, _) => EditRaw();

        var whatChanged = UiKit.Btn("What changed…", 130);
        whatChanged.Click += (_, _) => ShowChanges();

        actions.Controls.Add(save);
        actions.Controls.Add(reload);
        actions.Controls.Add(preview);
        actions.Controls.Add(rawEdit);
        actions.Controls.Add(whatChanged);

        bar.Controls.Add(searchLabel);
        bar.Controls.Add(_search);
        bar.Controls.Add(_onlySet);
        bar.Controls.Add(actions);
        bar.Controls.Add(UiKit.Rule());

        _tip.SetToolTip(_search, "Filters by cvar name, label, or description.");
        _tip.SetToolTip(_onlySet, "Show only cvars that autoexec.cfg currently sets.");
        _tip.SetToolTip(preview, "See the exact file text a save would produce, before saving.");
        _tip.SetToolTip(whatChanged, "Shows only the lines that would change, compared with the file on disk.");
        _tip.SetToolTip(rawEdit,
            "Edit the whole file as text — for aliases, binds and anything this page does not cover.\r\n" +
            "The settings above reload from whatever you apply here.");
        _tip.SetToolTip(save, "Writes autoexec.cfg. The previous version is backed up first. (Ctrl+S)");

        return bar;
    }

    private Control BuildConflictPanel()
    {
        _conflictPanel.Controls.Add(_conflictText);
        _conflictPanel.Controls.Add(new Panel { Dock = DockStyle.Top, Height = 1, BackColor = UiKit.Divider });
        return _conflictPanel;
    }

    // ---------------------------------------------------------------------------------------
    // Load
    // ---------------------------------------------------------------------------------------

    protected override void ReloadCore()
    {
        if (!Services.Paths.IsValid)
        {
            _host.Controls.Clear();
            _host.Controls.Add(MissingPathNotice());
            _conflictPanel.Visible = false;
            _built = false;
            return;
        }

        if (!_built)
        {
            _host.Controls.Clear();
            _host.Controls.Add(_editor);
            _host.Controls.Add(BuildIntro());
            _built = true;
        }

        _rawOnLoad = SafeWriter.Read(Services.Paths.AutoexecCfg);
        _editor.LoadFrom(CommandCfg.Parse(_rawOnLoad));

        RefreshConflicts();
        ApplyFilter();

        var exists = File.Exists(Services.Paths.AutoexecCfg);
        SetStatus(exists
            ? $"Loaded {Services.Paths.AutoexecCfg} — {_editor.SetCount} of {_editor.TotalCount} known settings present."
            : $"No autoexec.cfg yet. Saving will create {Services.Paths.AutoexecCfg}.");
    }

    private Control BuildIntro()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 54,
            BackColor = UiKit.PanelBg,
            Padding = new Padding(12, 8, 12, 8),
        };

        panel.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Font = UiKit.SmallFont,
            ForeColor = UiKit.Muted,
            Text =
                "These are console variables written to overrides\\autoexec.cfg, which mastercomfig runs last — " +
                "so anything set here wins over the preset and over modules.cfg.\r\n" +
                "Leave a setting blank to keep it out of the file entirely and let mastercomfig decide.",
        });

        panel.Controls.Add(UiKit.Rule());
        return panel;
    }

    // ---------------------------------------------------------------------------------------
    // Conflicts with modules.cfg
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Flags cvars a mastercomfig module already controls. Setting them here is legal and
    /// sometimes intended, but the module then does nothing — easy to miss in-game and easy to
    /// blame on the wrong setting later.
    /// </summary>
    private void RefreshConflicts()
    {
        var conflicts = _editor.ModuleConflicts.ToList();
        _conflictPanel.Visible = conflicts.Count > 0;
        if (conflicts.Count == 0) return;

        var pairs = conflicts
            .Select(r => $"{r.Def.Name} → {r.Def.ManagedByModule}")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var modules = conflicts
            .Select(r => r.Def.ManagedByModule!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        _conflictText.Text =
            $"⚠ {conflicts.Count} setting(s) here override mastercomfig modules: {string.Join(",  ", pairs)}\r\n" +
            $"That works, but the {(modules.Count == 1 ? "module" : "modules")} " +
            $"{string.Join(", ", modules.Select(m => $"'{m}'"))} in modules.cfg " +
            $"{(modules.Count == 1 ? "has" : "have")} no effect while these lines exist. " +
            "Clear a setting here to hand control back to the module.";
    }

    private void ApplyFilter()
    {
        if (!_built) return;

        _editor.Filter(_search.Text.Trim(), _onlySet.Checked);
        SetStatus($"Showing {_editor.VisibleCount} of {_editor.TotalCount} settings  ·  " +
                  $"{_editor.SetCount} set in autoexec.cfg");
    }

    private void OnEdited(object? sender, EventArgs e)
    {
        MarkDirty();
        RefreshConflicts();
        if (_onlySet.Checked) ApplyFilter();
    }

    // ---------------------------------------------------------------------------------------
    // Save
    // ---------------------------------------------------------------------------------------

    private string BuildContents()
    {
        var cfg = CommandCfg.Parse(_rawOnLoad);
        _editor.ApplyTo(cfg);
        return cfg.Render();
    }

    private void PreviewFile()
    {
        if (!Services.Paths.IsValid) return;
        TextDialog.Preview(this, "autoexec.cfg preview", BuildContents());
    }

    private void ShowChanges()
    {
        if (!Services.Paths.IsValid) return;
        TextDialog.ShowDiff(this, "Changes to autoexec.cfg",
            SafeWriter.Read(Services.Paths.AutoexecCfg), BuildContents());
    }

    private void EditRaw()
    {
        if (!Services.Paths.IsValid) return;

        using var dialog = new TextDialog("Edit autoexec.cfg", BuildContents(), readOnly: false,
            note: "Applying replaces the working copy of the file. Nothing is written until you save.");

        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        // The edited text becomes the new base, then the pickers re-read from it so the two
        // views cannot drift apart.
        _rawOnLoad = dialog.Contents;
        _editor.LoadFrom(CommandCfg.Parse(_rawOnLoad));
        MarkDirty();
        RefreshConflicts();
        ApplyFilter();
        SetStatus("Raw text applied — not written to disk yet.");
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
            var backup = SafeWriter.Write(Services.Paths.AutoexecCfg, contents);
            ReportSaved(Services.Paths.AutoexecCfg, backup);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                "Could not write autoexec.cfg.\r\n\r\n" + ex.Message +
                "\r\n\r\nYour existing file is untouched.",
                "Save failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }

        _rawOnLoad = contents;
        _editor.LoadFrom(CommandCfg.Parse(_rawOnLoad));
        RefreshConflicts();
        ApplyFilter();
        return true;
    }

    private void ReloadWithPrompt()
    {
        if (IsDirty && MessageBox.Show(this, "Discard unsaved changes to autoexec.cfg?", "Reload",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            return;

        Reload();
    }
}
