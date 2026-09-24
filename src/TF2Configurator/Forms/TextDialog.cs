using System.Text;

namespace TF2Configurator.Forms;

/// <summary>Modal monospace text viewer/editor, used for file previews and raw editing.</summary>
internal sealed class TextDialog : Form
{
    private readonly TextBox _text;

    public TextDialog(string title, string content, bool readOnly, string? note = null)
    {
        Text = title;
        Size = new Size(760, 620);
        MinimumSize = new Size(480, 320);
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        MinimizeBox = false;

        _text = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            Font = UiKit.MonoFont,
            ReadOnly = readOnly,
            BackColor = Color.White,
            Text = content,
        };

        var buttons = new Panel { Dock = DockStyle.Bottom, Height = 46, Padding = new Padding(10, 8, 10, 8) };

        var close = UiKit.Btn(readOnly ? "Close" : "Cancel");
        close.Dock = DockStyle.Right;
        close.DialogResult = DialogResult.Cancel;

        buttons.Controls.Add(close);

        if (!readOnly)
        {
            var ok = UiKit.Btn("Apply");
            ok.Dock = DockStyle.Right;
            ok.DialogResult = DialogResult.OK;
            buttons.Controls.Add(new Panel { Dock = DockStyle.Right, Width = 8 });
            buttons.Controls.Add(ok);
            AcceptButton = ok;
        }

        CancelButton = close;

        Controls.Add(_text);
        Controls.Add(buttons);

        if (note is not null)
        {
            Controls.Add(new Label
            {
                Dock = DockStyle.Top,
                Height = 34,
                Text = note,
                ForeColor = UiKit.Muted,
                Font = UiKit.SmallFont,
                Padding = new Padding(10, 9, 10, 0),
                BackColor = UiKit.PanelBg,
            });
        }
    }

    public string Contents => _text.Text;

    /// <summary>Shows the exact bytes that would be written, so a save holds no surprises.</summary>
    public static void Preview(IWin32Window owner, string title, string content) =>
        new TextDialog(title, content, readOnly: true,
                note: "This is exactly what will be written to disk when you save.")
            .ShowDialog(owner);

    /// <summary>
    /// Shows the lines a save would change, compared with the file on disk — one context line
    /// either side of each change, long identical stretches dropped.
    /// </summary>
    public static void ShowDiff(IWin32Window owner, string title, string before, string after)
    {
        var left = SplitLines(before);
        var right = SplitLines(after);

        if (left.SequenceEqual(right))
        {
            MessageBox.Show(owner,
                "No changes — the file on disk already matches what saving would produce.",
                title, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine("--- on disk");
        sb.AppendLine("+++ after saving");

        foreach (var (kind, line) in Diff(left, right))
            sb.Append(kind).Append(' ').AppendLine(line);

        new TextDialog(title, sb.ToString(), readOnly: true,
                note: "Only changed lines are shown (− on disk, + after saving). Nothing has been written.")
            .ShowDialog(owner);
    }

    private static string[] SplitLines(string text) =>
        text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

    /// <summary>
    /// Line diff via longest-common-subsequence, emitting each changed line with a marker and
    /// one unchanged context line on either side of every change.
    /// </summary>
    private static IEnumerable<(char Kind, string Line)> Diff(string[] a, string[] b)
    {
        var n = a.Length;
        var m = b.Length;
        var dp = new int[n + 1, m + 1];

        for (var i = n - 1; i >= 0; i--)
            for (var j = m - 1; j >= 0; j--)
                dp[i, j] = a[i] == b[j]
                    ? dp[i + 1, j + 1] + 1
                    : Math.Max(dp[i + 1, j], dp[i, j + 1]);

        var ops = new List<(char Kind, string Line)>();
        var x = 0;
        var y = 0;
        while (x < n && y < m)
        {
            if (a[x] == b[y])
            {
                ops.Add((' ', a[x]));
                x++;
                y++;
            }
            else if (dp[x + 1, y] >= dp[x, y + 1])
            {
                ops.Add(('-', a[x]));
                x++;
            }
            else
            {
                ops.Add(('+', b[y]));
                y++;
            }
        }

        while (x < n) ops.Add(('-', a[x++]));
        while (y < m) ops.Add(('+', b[y++]));

        // Collapse unchanged runs that are not adjacent to any change.
        for (var i = 0; i < ops.Count; i++)
        {
            var prevChange = i > 0 && ops[i - 1].Kind != ' ';
            var nextChange = i + 1 < ops.Count && ops[i + 1].Kind != ' ';
            if (ops[i].Kind != ' ' || prevChange || nextChange)
                yield return ops[i];
        }
    }
}
