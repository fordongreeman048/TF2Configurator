using System.Diagnostics;
using TF2Configurator.Services;

namespace TF2Configurator.Forms;

/// <summary>
/// Lists the backups <see cref="SafeWriter"/> has taken, with preview and restore. Restoring
/// writes through <see cref="SafeWriter"/> itself, so the current file is backed up first —
/// a restore can be undone like any save.
/// </summary>
internal sealed class BackupBrowser : Form
{
    private readonly ListView _list = new()
    {
        Dock = DockStyle.Fill,
        View = View.Details,
        FullRowSelect = true,
        HideSelection = false,
        MultiSelect = false,
        BorderStyle = BorderStyle.None,
    };

    private readonly Button _preview = UiKit.Btn("Preview…", 100);
    private readonly Button _restore = UiKit.Btn("Restore…", 100);
    private readonly Button _openFolder = UiKit.Btn("Open folder", 110);
    private readonly Button _close = UiKit.Btn("Close");

    private readonly List<BackupEntry> _entries = new();

    /// <summary>Raised after a file was restored, with the path that changed on disk.</summary>
    public event EventHandler<string>? FileRestored;

    public BackupBrowser()
    {
        Text = "Backups";
        Size = new Size(760, 480);
        MinimumSize = new Size(520, 320);
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;

        _list.Columns.Add("Backup taken", 150);
        _list.Columns.Add("File", 320);
        _list.Columns.Add("Size (bytes)", 110);
        _list.SelectedIndexChanged += (_, _) => UpdateButtons();
        _list.DoubleClick += (_, _) => PreviewSelected();

        var buttons = new Panel { Dock = DockStyle.Bottom, Height = 46, Padding = new Padding(10, 8, 10, 8) };

        _preview.Click += (_, _) => PreviewSelected();
        _restore.Click += (_, _) => RestoreSelected();
        _openFolder.Click += (_, _) => OpenFolder();
        _close.Click += (_, _) => Close();
        _close.DialogResult = DialogResult.Cancel;

        var flow = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
        };
        flow.Controls.Add(_close);
        flow.Controls.Add(_restore);
        flow.Controls.Add(_preview);
        flow.Controls.Add(_openFolder);

        buttons.Controls.Add(flow);

        var tip = UiKit.NewToolTip();
        tip.SetToolTip(_preview, "View the backup's contents. Nothing changes.");
        tip.SetToolTip(_restore,
            "Copies this backup over the current file. The current file is itself backed up " +
            "first, so the restore can be undone.");
        tip.SetToolTip(_openFolder, "Open the backup folder in Explorer.");

        Controls.Add(_list);
        Controls.Add(buttons);

        LoadBackups();
        UpdateButtons();

        CancelButton = _close;
    }

    private void LoadBackups()
    {
        _entries.Clear();
        _list.BeginUpdate();
        _list.Items.Clear();

        try
        {
            if (!Directory.Exists(SafeWriter.BackupRoot)) return;

            var files = Directory.EnumerateFiles(SafeWriter.BackupRoot, "*", SearchOption.AllDirectories)
                .OrderByDescending(f => File.GetLastWriteTime(f))
                .ToList();

            foreach (var file in files)
            {
                var folder = Path.GetFileName(Path.GetDirectoryName(file) ?? "");
                var time = File.GetLastWriteTime(file);
                // SafeWriter keeps backups as <backup root>\<timestamp>\<original filename>, so
                // the backup file's path is the path the file lives at on disk.
                var entry = new BackupEntry(
                    Time: time,
                    FileName: Path.GetFileName(file),
                    Path: file);

                _entries.Add(entry);
                var item = new ListViewItem(time.ToString("yyyy-MM-dd HH:mm:ss")) { Tag = entry };
                item.SubItems.Add($"{folder}  \\  {entry.FileName}");
                item.SubItems.Add(new FileInfo(file).Length.ToString());
                _list.Items.Add(item);
            }
        }
        catch
        {
            // Unreadable backup store: show an empty list.
        }
        finally
        {
            _list.EndUpdate();
        }
    }

    private BackupEntry? Selected =>
        _list.SelectedItems.Count > 0 ? _list.SelectedItems[0].Tag as BackupEntry : null;

    private void UpdateButtons()
    {
        var has = Selected is not null;
        _preview.Enabled = has;
        _restore.Enabled = has;
    }

    private void PreviewSelected()
    {
        var entry = Selected;
        if (entry is null) return;

        var contents = SafeWriter.Read(entry.Path);
        using var dialog = new TextDialog("Backup — " + entry.FileName, contents, readOnly: true,
            note: "Backup taken " + entry.Time.ToString("yyyy-MM-dd HH:mm") +
                  ". Restoring overwrites the current file, which is itself backed up first.");
        dialog.ShowDialog(this);
    }

    private void RestoreSelected()
    {
        var entry = Selected;
        if (entry is null) return;

        var currentExists = File.Exists(entry.Path);
        var answer = MessageBox.Show(this,
            $"Restore {entry.FileName} from the backup taken {entry.Time:yyyy-MM-dd HH:mm}?\r\n\r\n" +
            (currentExists
                ? "The current file is backed up first, so this can be undone."
                : "The current file does not exist — restoring recreates it."),
            "Restore backup", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

        if (answer != DialogResult.Yes) return;

        try
        {
            var contents = SafeWriter.Read(entry.Path);
            SafeWriter.Write(entry.Path, contents);
            FileRestored?.Invoke(this, entry.Path);
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                "Could not restore the file.\r\n\r\n" + ex.Message +
                "\r\n\r\nYour current file is untouched.",
                "Restore failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OpenFolder()
    {
        try
        {
            Directory.CreateDirectory(SafeWriter.BackupRoot);
            Process.Start(new ProcessStartInfo("explorer.exe", SafeWriter.BackupRoot) { UseShellExecute = true });
        }
        catch
        {
            // Opening Explorer is a convenience; failure is not worth a dialog.
        }
    }

    private sealed record BackupEntry(
        DateTime Time,
        string FileName,
        string Path);
}
