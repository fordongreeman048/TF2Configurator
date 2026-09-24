using TF2Configurator.Models;
using TF2Configurator.Services;

namespace TF2Configurator.Forms;

/// <summary>
/// Editor for the per-class files in <c>tf\cfg\overrides\</c> — <c>scout.cfg</c>,
/// <c>soldier.cfg</c> and so on. TF2 runs them when you switch class, so they're the right place
/// for per-class settings (viewmodels, sensitivity, crosshair).
/// Only one class is loaded at a time, and switching classes behaves like switching files:
/// unsaved work is confirmed first, since a silent discard would lose edits for good.
/// </summary>
internal sealed class ClassConfigPage : ConfigPage
{
    private readonly ToolTip _tip = UiKit.NewToolTip();
    private readonly CvarEditor _editor;

    private readonly ComboBox _classPicker = new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
        FlatStyle = FlatStyle.System,
        Width = 160,
    };

    private readonly Label _fileLabel = new()
    {
        AutoSize = true,
        Font = UiKit.SmallFont,
        ForeColor = UiKit.Muted,
    };

    private readonly TextBox _search = new() { Width = 180 };
    private readonly CheckBox _onlySet = new() { Text = "Only settings in file", AutoSize = true };
    private readonly Panel _host = new() { Dock = DockStyle.Fill };

    private string _rawOnLoad = "";
    private int _currentClass;
    private bool _switching;
    private bool _built;

    public ClassConfigPage(AppContextServices services) : base(services)
    {
        _editor = new CvarEditor(CvarCatalog.ClassConfig, _tip);
        _editor.ValueEdited += OnEdited;

        foreach (var (_, display) in TF2Paths.Classes)
            _classPicker.Items.Add(display);
        _classPicker.SelectedIndex = 0;
        _classPicker.SelectedIndexChanged += OnClassChanged;

        Controls.Add(_host);
        Controls.Add(BuildToolbar());

        _search.TextChanged += (_, _) => ApplyFilter();
        _onlySet.CheckedChanged += (_, _) => ApplyFilter();
    }

    public override string Title => "Class configs";

    private string ClassFile => TF2Paths.Classes[_currentClass].File;
    private string ClassDisplay => TF2Paths.Classes[_currentClass].Display;
    private string ClassPath => Services.Paths.ClassCfg(ClassFile);

    // ---------------------------------------------------------------------------------------
    // Layout
    // ---------------------------------------------------------------------------------------

    private Control BuildToolbar()
    {
        var bar = new Panel
        {
            Dock = DockStyle.Top,
            Height = 84,
            BackColor = Color.White,
        };

        var classLabel = new Label
        {
            Text = "Class:",
            Font = UiKit.BoldFont,
            AutoSize = true,
            Location = new Point(12, 15),
        };

        _classPicker.Location = new Point(62, 11);
        _fileLabel.Location = new Point(236, 16);

        var searchLabel = new Label { Text = "Search:", AutoSize = true, Location = new Point(12, 53) };
        _search.Location = new Point(62, 49);
        _onlySet.Location = new Point(258, 52);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            WrapContents = false,
            Padding = new Padding(0, 26, 12, 0),
        };

        var save = UiKit.Btn("Save class config", 150);
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

        bar.Controls.Add(classLabel);
        bar.Controls.Add(_classPicker);
        bar.Controls.Add(_fileLabel);
        bar.Controls.Add(searchLabel);
        bar.Controls.Add(_search);
        bar.Controls.Add(_onlySet);
        bar.Controls.Add(actions);
        bar.Controls.Add(UiKit.Rule());

        _tip.SetToolTip(_classPicker,
            "Each class runs its own cfg when you switch to it, on top of autoexec.cfg.");
        _tip.SetToolTip(rawEdit,
            "Edit the whole file as text — for binds, aliases and anything this page does not cover.");
        _tip.SetToolTip(whatChanged, "Shows only the lines that would change, compared with the file on disk.");
        _tip.SetToolTip(save, "Writes this class's cfg. The previous version is backed up first. (Ctrl+S)");

        return bar;
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
                "A class cfg runs every time you switch to that class, so whatever it sets stays set " +
                "until another class cfg changes it.\r\n" +
                "That means a setting you only want on one class should be set on all nine — otherwise it " +
                "leaks into the classes that leave it alone.",
        });

        panel.Controls.Add(UiKit.Rule());
        return panel;
    }

    // ---------------------------------------------------------------------------------------
    // Class switching
    // ---------------------------------------------------------------------------------------

    private void OnClassChanged(object? sender, EventArgs e)
    {
        if (_switching) return;

        var target = _classPicker.SelectedIndex;
        if (target == _currentClass) return;

        if (IsDirty)
        {
            var answer = MessageBox.Show(this,
                $"Unsaved changes to {ClassDisplay}.\r\n\r\n" +
                "Save them before switching class?",
                "Unsaved changes", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);

            if (answer == DialogResult.Cancel || (answer == DialogResult.Yes && !Save()))
            {
                // Put the picker back without re-entering this handler.
                _switching = true;
                _classPicker.SelectedIndex = _currentClass;
                _switching = false;
                return;
            }
        }

        _currentClass = target;
        Reload();
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
            _fileLabel.Text = "";
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

        _switching = true;
        _classPicker.SelectedIndex = _currentClass;
        _switching = false;

        _rawOnLoad = SafeWriter.Read(ClassPath);
        _editor.LoadFrom(CommandCfg.Parse(_rawOnLoad));

        var exists = File.Exists(ClassPath);
        _fileLabel.Text = exists
            ? $"overrides\\{ClassFile}.cfg  ·  {_editor.SetCount} setting(s)"
            : $"overrides\\{ClassFile}.cfg  ·  does not exist yet";
        _fileLabel.ForeColor = exists ? UiKit.Muted : UiKit.WarnText;

        ApplyFilter();

        SetStatus(exists
            ? $"Loaded {ClassPath}"
            : $"{ClassDisplay} has no config yet. Saving will create {ClassPath}.");
    }

    private void ApplyFilter()
    {
        if (!_built) return;

        _editor.Filter(_search.Text.Trim(), _onlySet.Checked);
        SetStatus($"{ClassDisplay}  ·  showing {_editor.VisibleCount} of {_editor.TotalCount} settings  ·  " +
                  $"{_editor.SetCount} set");
    }

    private void OnEdited(object? sender, EventArgs e)
    {
        MarkDirty();
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
        TextDialog.Preview(this, $"{ClassFile}.cfg preview", BuildContents());
    }

    private void ShowChanges()
    {
        if (!Services.Paths.IsValid) return;
        TextDialog.ShowDiff(this, $"Changes to {ClassFile}.cfg",
            SafeWriter.Read(ClassPath), BuildContents());
    }

    private void EditRaw()
    {
        if (!Services.Paths.IsValid) return;

        using var dialog = new TextDialog($"Edit {ClassFile}.cfg", BuildContents(), readOnly: false,
            note: "Applying replaces the working copy of the file. Nothing is written until you save.");

        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        _rawOnLoad = dialog.Contents;
        _editor.LoadFrom(CommandCfg.Parse(_rawOnLoad));
        MarkDirty();
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

        // An empty file is not worth creating; if the user cleared everything, offer to delete.
        if (contents.Trim().Length == 0 && File.Exists(ClassPath))
        {
            var answer = MessageBox.Show(this,
                $"{ClassFile}.cfg would be empty.\r\n\r\nDelete the file instead of writing a blank one?",
                "Empty config", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);

            if (answer == DialogResult.Cancel) return false;
            if (answer == DialogResult.Yes)
            {
                try
                {
                    // Back the contents up first so the delete is recoverable.
                    var backup = SafeWriter.Write(ClassPath, SafeWriter.Read(ClassPath));
                    File.Delete(ClassPath);
                    ReportSaved(ClassPath, backup);
                    SetStatus($"Deleted {ClassPath}");
                    _rawOnLoad = "";
                    _editor.LoadFrom(CommandCfg.Parse(_rawOnLoad));
                    ApplyFilter();
                    return true;
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Could not delete the file.\r\n\r\n" + ex.Message,
                        "Delete failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return false;
                }
            }
        }

        try
        {
            var backup = SafeWriter.Write(ClassPath, contents);
            ReportSaved(ClassPath, backup);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                $"Could not write {ClassFile}.cfg.\r\n\r\n" + ex.Message +
                "\r\n\r\nYour existing file is untouched.",
                "Save failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }

        _rawOnLoad = contents;
        _editor.LoadFrom(CommandCfg.Parse(_rawOnLoad));
        _fileLabel.Text = $"overrides\\{ClassFile}.cfg  ·  {_editor.SetCount} setting(s)";
        _fileLabel.ForeColor = UiKit.Muted;
        ApplyFilter();
        return true;
    }

    private void ReloadWithPrompt()
    {
        if (IsDirty && MessageBox.Show(this, $"Discard unsaved changes to {ClassFile}.cfg?", "Reload",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            return;

        Reload();
    }
}
