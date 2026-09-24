using TF2Configurator.Models;
using TF2Configurator.Services;

namespace TF2Configurator.Forms;

/// <summary>
/// Base for each tab. Pages own their own file, know whether they have unsaved edits, and
/// report progress back to the shell rather than popping their own message boxes.
/// </summary>
internal abstract class ConfigPage : UserControl
{
    protected ConfigPage(AppContextServices services)
    {
        Services = services;
        Dock = DockStyle.Fill;
        BackColor = Color.White;
    }

    protected AppContextServices Services { get; }

    public abstract string Title { get; }

    /// <summary>True when the page has edits that are not on disk yet.</summary>
    public bool IsDirty { get; private set; }

    /// <summary>Raised when dirty state changes, so the shell can update the tab text.</summary>
    public event EventHandler? DirtyChanged;

    /// <summary>Raised to put a message in the status bar.</summary>
    public event EventHandler<string>? StatusChanged;

    protected void MarkDirty(bool dirty = true)
    {
        if (IsDirty == dirty) return;
        IsDirty = dirty;
        DirtyChanged?.Invoke(this, EventArgs.Empty);
    }

    protected void SetStatus(string message) => StatusChanged?.Invoke(this, message);

    /// <summary>Re-reads from disk, discarding in-memory edits.</summary>
    public void Reload()
    {
        ReloadCore();
        MarkDirty(false);
    }

    /// <summary>Writes to disk. Returns false if the page declined to save.</summary>
    public bool Save()
    {
        if (!SaveCore()) return false;
        MarkDirty(false);
        return true;
    }

    protected abstract void ReloadCore();
    protected abstract bool SaveCore();

    /// <summary>Reports the backup location after a successful write.</summary>
    protected void ReportSaved(string path, string? backup)
    {
        SetStatus(backup is null
            ? $"Saved {Path.GetFileName(path)} — no changes to back up."
            : $"Saved {Path.GetFileName(path)} — previous version backed up to {backup}");
    }

    /// <summary>Shown by pages that need a valid TF2 path before they can do anything.</summary>
    protected static Control MissingPathNotice() => new Label
    {
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleCenter,
        ForeColor = UiKit.BadText,
        Padding = new Padding(40),
        Text = "Team Fortress 2 was not found.\r\n\r\n" +
               "Use \"Change…\" at the top of the window to point at your\r\n" +
               "\"Team Fortress 2\" folder (the one containing tf\\ and hl2.exe).",
    };
}

/// <summary>Shared state handed to every page: paths, catalog, preset data and settings.</summary>
internal sealed class AppContextServices
{
    public required TF2Paths Paths { get; set; }
    public required ModuleCatalog Catalog { get; set; }
    public required PresetCatalog Presets { get; set; }
    public required CatalogService CatalogService { get; init; }
    public required AppSettings Settings { get; init; }
}
