using TF2Configurator.Models;
using TF2Configurator.Services;

namespace TF2Configurator.Forms;

internal sealed class MainForm : Form
{
    private readonly AppContextServices _services;
    private readonly TabControl _tabs = new() { Dock = DockStyle.Fill };
    private readonly List<ConfigPage> _pages = new();

    private readonly Label _pathLabel = new() { AutoSize = true, Font = UiKit.BoldFont };
    private readonly Label _detailLabel = new() { AutoSize = true, ForeColor = UiKit.Muted, Font = UiKit.SmallFont };
    private readonly Label _catalogLabel = new() { AutoSize = true, ForeColor = UiKit.Muted, Font = UiKit.SmallFont };
    private readonly StatusStrip _status = new();
    private readonly ToolStripStatusLabel _statusText = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Button _refreshCatalog = UiKit.Btn("Refresh catalog", 130);

    public MainForm()
    {
        Text = "TF2 Configurator";
        MinimumSize = new Size(960, 660);
        Size = new Size(1180, 800);
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = true;

        var settings = AppSettings.Load();
        var catalogService = new CatalogService();

        ModuleCatalog catalog;
        try
        {
            catalog = catalogService.Load();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "The module catalog could not be loaded, so the modules editor cannot run.\r\n\r\n" + ex.Message,
                "Catalog error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            catalog = new ModuleCatalog();
        }

        _services = new AppContextServices
        {
            Paths = TF2Paths.Detect(settings.Tf2PathOverride),
            Catalog = catalog,
            Presets = catalogService.LoadPresets(),
            CatalogService = catalogService,
            Settings = settings,
        };

        Controls.Add(_tabs);
        Controls.Add(BuildHeader());
        Controls.Add(BuildStatusBar());

        BuildPages();
        UpdateHeader();

        _tabs.SelectedIndexChanged += (_, _) => SetStatus("");
        FormClosing += OnFormClosing;
        KeyDown += OnKeyDown;
        Shown += async (_, _) => await CheckForCatalogUpdatesAsync();
    }

    // ---------------------------------------------------------------------------------------
    // Chrome
    // ---------------------------------------------------------------------------------------

    private Control BuildHeader()
    {
        var header = new Panel
        {
            Dock = DockStyle.Top,
            Height = 84,
            BackColor = UiKit.PanelBg,
            Padding = new Padding(14, 10, 14, 0),
        };

        _pathLabel.Location = new Point(14, 10);
        _detailLabel.Location = new Point(16, 32);
        _catalogLabel.Location = new Point(16, 52);

        var change = UiKit.Btn("Change…", 100);
        change.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        change.Location = new Point(header.Width - 118, 10);
        change.Click += (_, _) => ChooseGameFolder();

        _refreshCatalog.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _refreshCatalog.Location = new Point(header.Width - 148, 44);
        _refreshCatalog.Click += async (_, _) => await RefreshCatalogAsync();

        var backups = UiKit.Btn("Backups…", 100);
        backups.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        backups.Location = new Point(header.Width - 256, 44);
        backups.Click += (_, _) => ShowBackups();

        header.Controls.Add(_pathLabel);
        header.Controls.Add(_detailLabel);
        header.Controls.Add(_catalogLabel);
        header.Controls.Add(change);
        header.Controls.Add(_refreshCatalog);
        header.Controls.Add(backups);
        header.Controls.Add(UiKit.Rule());

        var tip = UiKit.NewToolTip();
        tip.SetToolTip(change, "Point at your \"Team Fortress 2\" folder — the one containing tf\\ and hl2.exe.");
        tip.SetToolTip(_refreshCatalog,
            "Downloads the current module list from mastercomfig's repository.\r\n" +
            "The bundled copy keeps working if this fails or you are offline.");
        tip.SetToolTip(backups,
            "Browse the backups this app makes before every save, and restore any of them.\r\n" +
            "Restoring backs up the current file first, so it can be undone.");

        return header;
    }

    private Control BuildStatusBar()
    {
        _status.Items.Add(_statusText);

        var about = new ToolStripStatusLabel("About TF2 Configurator")
        {
            IsLink = true,
            Alignment = ToolStripItemAlignment.Right,
            Margin = new Padding(8, 0, 4, 0),
        };
        about.Click += (_, _) => ShowAbout();
        _status.Items.Add(about);

        _status.SizingGrip = true;
        return _status;
    }

    private static void ShowAbout()
    {
        var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)
                      ?? "unknown";
        MessageBox.Show(
            "TF2 Configurator " + version + "\r\n\r\n" +
            "A config editor for mastercomfig in Team Fortress 2.\r\n\r\n" +
            "Every save is backed up to:\r\n" +
            Services.SafeWriter.BackupRoot + "\r\n\r\n" +
            "The module catalog is bundled and can be refreshed from mastercomfig's repository.",
            "About TF2 Configurator", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void BuildPages()
    {
        _pages.Clear();
        _tabs.TabPages.Clear();

        AddPage(new ModulesPage(_services));
        AddPage(new AutoexecPage(_services));
        AddPage(new ClassConfigPage(_services));
        AddPage(new LaunchOptionsPage(_services));
    }

    private void AddPage(ConfigPage page)
    {
        var tab = new TabPage(page.Title) { Padding = new Padding(0), BackColor = Color.White };
        tab.Controls.Add(page);
        _tabs.TabPages.Add(tab);
        _pages.Add(page);

        page.StatusChanged += (_, msg) => SetStatus(msg);
        page.DirtyChanged += (_, _) =>
        {
            tab.Text = page.IsDirty ? page.Title + " *" : page.Title;
            UpdateTitle();
        };

        page.Reload();
    }

    private void UpdateHeader()
    {
        var p = _services.Paths;

        if (p.IsValid)
        {
            _pathLabel.Text = "Team Fortress 2: " + p.GameRoot;
            _pathLabel.ForeColor = Color.FromArgb(30, 30, 36);

            var status = p.GetMastercomfigStatus();
            _detailLabel.Text = $"Detected via {p.DetectionSource}   |   {status.Summary}";
            _detailLabel.ForeColor = status.IsInstalled && status.Presets.Count == 0
                ? UiKit.WarnText
                : UiKit.Muted;
        }
        else
        {
            _pathLabel.Text = "Team Fortress 2 not found";
            _pathLabel.ForeColor = UiKit.BadText;
            _detailLabel.Text = "Use \"Change…\" to select your Team Fortress 2 folder.";
            _detailLabel.ForeColor = UiKit.BadText;
        }

        _catalogLabel.Text =
            $"Module catalog: {_services.CatalogService.Source}   |   " +
            $"{_services.Catalog.ModuleCount} modules in {_services.Catalog.Categories.Count} categories";

        UpdateTitle();
    }

    private void UpdateTitle()
    {
        var dirty = _pages.Any(p => p.IsDirty);
        Text = dirty ? "TF2 Configurator — unsaved changes" : "TF2 Configurator";
    }

    private void SetStatus(string message) => _statusText.Text = message;

    // ---------------------------------------------------------------------------------------
    // Actions
    // ---------------------------------------------------------------------------------------

    private void ChooseGameFolder()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Select your \"Team Fortress 2\" folder (contains tf\\ and hl2.exe).",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false,
            SelectedPath = _services.Paths.GameRoot ?? "",
        };

        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        var detected = TF2Paths.Detect(dialog.SelectedPath);
        if (!detected.IsValid)
        {
            MessageBox.Show(this,
                "That folder does not look like a Team Fortress 2 install.\r\n\r\n" +
                "Expected to find a \"tf\" subfolder alongside hl2.exe or tf_win64.exe.",
                "Not a TF2 folder", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (!ConfirmDiscardIfDirty("Switching folders will discard them.")) return;

        _services.Paths = detected;
        _services.Settings.Tf2PathOverride = detected.GameRoot;
        _services.Settings.Save();

        foreach (var page in _pages) page.Reload();
        UpdateHeader();
        SetStatus("Now editing configs in " + detected.OverridesDir);
    }

    private void ShowBackups()
    {
        using var dialog = new BackupBrowser();
        dialog.FileRestored += (_, path) =>
        {
            var p = _services.Paths;
            ConfigPage? page = null;

            if (string.Equals(path, p.ModulesCfg, StringComparison.OrdinalIgnoreCase))
                page = _pages.OfType<ModulesPage>().FirstOrDefault();
            else if (string.Equals(path, p.AutoexecCfg, StringComparison.OrdinalIgnoreCase))
                page = _pages.OfType<AutoexecPage>().FirstOrDefault();
            else if (TF2Paths.Classes.Any(c =>
                         string.Equals(path, p.ClassCfg(c.File), StringComparison.OrdinalIgnoreCase)))
                page = _pages.OfType<ClassConfigPage>().FirstOrDefault();

            page?.Reload();
            SetStatus("Restored " + Path.GetFileName(path) + " from backup.");
        };

        dialog.ShowDialog(this);
    }

    private async Task RefreshCatalogAsync()
    {
        if (!ConfirmDiscardIfDirty("Refreshing the catalog reloads every page and will discard them."))
            return;

        _refreshCatalog.Enabled = false;
        SetStatus("Downloading module catalog from mastercomfig…");

        try
        {
            _services.Catalog = await _services.CatalogService.RefreshAsync();
            _services.Presets = _services.CatalogService.LoadPresets();
            foreach (var page in _pages) page.Reload();
            UpdateHeader();
            SetStatus($"Catalog updated — {_services.Catalog.ModuleCount} modules.");
        }
        catch (Exception ex)
        {
            SetStatus("Catalog refresh failed — still using " + _services.CatalogService.Source);
            MessageBox.Show(this,
                "Could not download the module catalog.\r\n\r\n" + ex.Message +
                "\r\n\r\nThe previously loaded catalog is still in use, so nothing is broken.",
                "Refresh failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            _refreshCatalog.Enabled = true;
        }
    }

    /// <summary>
    /// Quiet catalog check after the window shows: downloads and caches a fresh copy at most
    /// every few days, and only swaps the in-memory catalog when nothing is being edited. A
    /// failure is ignored — the bundled snapshot keeps working, not worth a dialog.
    /// </summary>
    private async Task CheckForCatalogUpdatesAsync()
    {
        try
        {
            var settings = _services.Settings;
            if (settings.LastCatalogCheck is not null &&
                DateTime.Now - settings.LastCatalogCheck.Value < TimeSpan.FromDays(3))
                return;

            // RefreshAsync validates and caches; it does not touch the UI by itself.
            var fresh = await _services.CatalogService.RefreshAsync();
            settings.LastCatalogCheck = DateTime.Now;
            settings.Save();

            if (_pages.Any(p => p.IsDirty)) return;

            _services.Catalog = fresh;
            _services.Presets = _services.CatalogService.LoadPresets();
            foreach (var page in _pages) page.Reload();
            UpdateHeader();
            SetStatus($"Catalog updated in the background — {fresh.ModuleCount} modules.");
        }
        catch
        {
            // The bundled snapshot keeps working; a failed check is not worth surfacing.
        }
    }

    private bool ConfirmDiscardIfDirty(string consequence)
    {
        var dirty = _pages.Where(p => p.IsDirty).Select(p => p.Title).ToList();
        if (dirty.Count == 0) return true;

        var answer = MessageBox.Show(this,
            "Unsaved changes on: " + string.Join(", ", dirty) + "\r\n\r\n" + consequence +
            "\r\n\r\nContinue?",
            "Unsaved changes", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        return answer == DialogResult.Yes;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Control && e.KeyCode == Keys.S)
        {
            e.Handled = true;
            if (_tabs.SelectedIndex >= 0 && _tabs.SelectedIndex < _pages.Count)
                _pages[_tabs.SelectedIndex].Save();
        }
        else if (e.KeyCode == Keys.F5)
        {
            e.Handled = true;
            if (_tabs.SelectedIndex >= 0 && _tabs.SelectedIndex < _pages.Count)
            {
                var page = _pages[_tabs.SelectedIndex];
                if (!page.IsDirty ||
                    MessageBox.Show(this, "Discard unsaved changes on this page?", "Reload",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
                {
                    page.Reload();
                    SetStatus("Reloaded from disk.");
                }
            }
        }
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        var dirty = _pages.Where(p => p.IsDirty).Select(p => p.Title).ToList();
        if (dirty.Count == 0) return;

        var answer = MessageBox.Show(this,
            "These pages have unsaved changes:\r\n\r\n  " + string.Join("\r\n  ", dirty) +
            "\r\n\r\nSave them before closing?",
            "Unsaved changes", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);

        switch (answer)
        {
            case DialogResult.Cancel:
                e.Cancel = true;
                break;
            case DialogResult.Yes:
                foreach (var page in _pages.Where(p => p.IsDirty))
                {
                    if (!page.Save()) e.Cancel = true;
                }
                break;
        }
    }
}
