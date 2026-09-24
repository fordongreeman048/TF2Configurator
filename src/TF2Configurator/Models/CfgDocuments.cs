using System.Text;

namespace TF2Configurator.Models;

/// <summary>
/// Line splitting and re-joining, shared by both config documents. A file's own line
/// endings matter: if it was saved with LF and no trailing newline, rewriting it as CRLF
/// would change every line just to edit one setting.
/// </summary>
internal static class CfgText
{
    public const string DefaultNewline = "\r\n";

    /// <summary>The dominant line terminator in <paramref name="text"/>, CRLF when there is none.</summary>
    public static string DetectNewline(string text) =>
        text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n"
        : text.Contains('\n') ? "\n"
        : text.Contains('\r') ? "\r"
        : DefaultNewline;

    /// <summary>True when the text ends with a line terminator.</summary>
    public static bool EndsWithNewline(string text) =>
        text.Length > 0 && (text[^1] == '\n' || text[^1] == '\r');

    /// <summary>Splits on any terminator. A final newline yields a trailing empty element.</summary>
    public static string[] SplitLines(string text) =>
        text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

    /// <summary>
    /// Index of the first <c>//</c> that sits outside a double-quoted run, or -1.
    /// A comment marker inside quotes is literal text (e.g. a URL), not a comment.
    /// </summary>
    public static int FindCommentStart(string raw)
    {
        var quoted = false;
        for (var i = 0; i < raw.Length - 1; i++)
        {
            if (raw[i] == '"')
            {
                quoted = !quoted;
                continue;
            }

            if (!quoted && raw[i] == '/' && raw[i + 1] == '/') return i;
        }

        return -1;
    }
}

/// <summary>
/// A line-preserving editor for <c>key=value</c> config files (mastercomfig's modules.cfg).
/// Every original line — comments, blanks, unknown keys, odd spacing — is kept unless it is
/// actually changed. modules.cfg often holds hand-written comments or stale values; dropping
/// them silently would be rude.
/// </summary>
public sealed class KeyValueCfg
{
    public sealed class Line
    {
        /// <summary>Original text, minus the line terminator.</summary>
        public string Raw = "";

        /// <summary>Key when this line is a key=value assignment, otherwise null.</summary>
        public string? Key;

        /// <summary>Value when this line is a key=value assignment, otherwise null.</summary>
        public string? Value;

        /// <summary>Trailing <c>// ...</c> comment, including the slashes. Null when absent.</summary>
        public string? TrailingComment;

        public bool IsAssignment => Key is not null;
    }

    private readonly List<Line> _lines = new();

    /// <summary>Terminator the file used, reproduced on save.</summary>
    private string _newline = CfgText.DefaultNewline;

    /// <summary>
    /// Whether the file ended with a terminator. New files get one; an existing file keeps
    /// whatever it had, so saving does not append a phantom blank line.
    /// </summary>
    private bool _finalNewline = true;

    public IReadOnlyList<Line> Lines => _lines;

    /// <summary>Assignments in file order.</summary>
    public IEnumerable<(string Key, string Value)> Entries =>
        _lines.Where(l => l.IsAssignment).Select(l => (l.Key!, l.Value ?? ""));

    public static KeyValueCfg Parse(string? text)
    {
        var doc = new KeyValueCfg();
        if (string.IsNullOrEmpty(text)) return doc;

        doc._newline = CfgText.DetectNewline(text);
        doc._finalNewline = CfgText.EndsWithNewline(text);

        foreach (var rawLine in CfgText.SplitLines(text))
            doc._lines.Add(ParseLine(rawLine));

        // A trailing newline yields one empty element; drop it, since _finalNewline records it.
        if (doc._finalNewline && doc._lines.Count > 0 && doc._lines[^1].Raw.Length == 0)
            doc._lines.RemoveAt(doc._lines.Count - 1);

        return doc;
    }

    private static Line ParseLine(string raw)
    {
        var line = new Line { Raw = raw };
        var trimmed = raw.Trim();

        if (trimmed.Length == 0 || trimmed.StartsWith("//", StringComparison.Ordinal))
            return line; // blank or comment-only

        // Split off a trailing comment before looking for '='.
        var code = raw;
        var cmt = CfgText.FindCommentStart(raw);
        if (cmt >= 0)
        {
            line.TrailingComment = raw[cmt..];
            code = raw[..cmt];
        }

        var eq = code.IndexOf('=');
        if (eq <= 0) return line; // not an assignment we understand; preserve as-is

        var key = code[..eq].Trim();
        var value = code[(eq + 1)..].Trim();
        if (key.Length == 0) return line;

        line.Key = key;
        line.Value = value;
        return line;
    }

    public bool Has(string key) => FindLine(key) is not null;

    public string? Get(string key) => FindLine(key)?.Value;

    /// <summary>
    /// Finds the last assignment of <paramref name="key"/>. With duplicate keys the engine
    /// takes the last value, so that is the one that matters here.
    /// </summary>
    private Line? FindLine(string key)
    {
        for (var i = _lines.Count - 1; i >= 0; i--)
        {
            var l = _lines[i];
            if (l.IsAssignment &&
                string.Equals(l.Key, key, StringComparison.OrdinalIgnoreCase))
                return l;
        }

        return null;
    }

    /// <summary>
    /// Sets a key. Updates the last assignment in place (the one the engine reads);
    /// appends at the end when the key is new.
    /// </summary>
    public void Set(string key, string value)
    {
        var existing = FindLine(key);
        if (existing is not null)
        {
            existing.Value = value;
            existing.Raw = Compose(existing.Key!, value, existing.TrailingComment);
            return;
        }

        _lines.Add(new Line
        {
            Key = key,
            Value = value,
            Raw = Compose(key, value, null),
        });
    }

    /// <summary>
    /// Removes every assignment of the key, so mastercomfig falls back to the preset default.
    /// One leftover duplicate would still be read by the engine, so "removed" has to mean
    /// "gone entirely".
    /// </summary>
    public bool Remove(string key)
    {
        var removed = false;
        for (var i = _lines.Count - 1; i >= 0; i--)
        {
            var l = _lines[i];
            if (l.IsAssignment &&
                string.Equals(l.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                _lines.RemoveAt(i);
                removed = true;
            }
        }

        return removed;
    }

    private static string Compose(string key, string value, string? trailingComment) =>
        trailingComment is null ? $"{key}={value}" : $"{key}={value} {trailingComment}";

    public string Render()
    {
        var sb = new StringBuilder();
        for (var i = 0; i < _lines.Count; i++)
        {
            sb.Append(_lines[i].Raw);
            if (i < _lines.Count - 1 || _finalNewline) sb.Append(_newline);
        }

        return sb.ToString();
    }
}

/// <summary>
/// Same idea as <see cref="KeyValueCfg"/>, for command-style Source configs (autoexec.cfg,
/// scout.cfg, ...) where a line is <c>cvar value</c> instead of <c>key=value</c>.
/// Aliases, binds, comments and anything unrecognised are kept as-is.
/// </summary>
public sealed class CommandCfg
{
    public sealed class Line
    {
        public string Raw = "";
        public string? Cvar;
        public string? Value;
        public string? TrailingComment;
        public bool IsAssignment => Cvar is not null;
    }

    private readonly List<Line> _lines = new();

    private string _newline = CfgText.DefaultNewline;
    private bool _finalNewline = true;

    public IReadOnlyList<Line> Lines => _lines;

    public IEnumerable<(string Cvar, string Value)> Entries =>
        _lines.Where(l => l.IsAssignment).Select(l => (l.Cvar!, l.Value ?? ""));

    /// <summary>Commands that take arguments but must never be treated as a settable cvar.</summary>
    private static readonly HashSet<string> NotCvars = new(StringComparer.OrdinalIgnoreCase)
    {
        "bind", "unbind", "unbindall", "alias", "exec", "echo", "incrementvar",
        "toggle", "bindtoggle", "+attack", "-attack", "impulse", "say", "say_team",
    };

    public static CommandCfg Parse(string? text)
    {
        var doc = new CommandCfg();
        if (string.IsNullOrEmpty(text)) return doc;

        doc._newline = CfgText.DetectNewline(text);
        doc._finalNewline = CfgText.EndsWithNewline(text);

        foreach (var rawLine in CfgText.SplitLines(text))
            doc._lines.Add(ParseLine(rawLine));

        if (doc._finalNewline && doc._lines.Count > 0 && doc._lines[^1].Raw.Length == 0)
            doc._lines.RemoveAt(doc._lines.Count - 1);

        return doc;
    }

    private static Line ParseLine(string raw)
    {
        var line = new Line { Raw = raw };
        var trimmed = raw.Trim();

        if (trimmed.Length == 0 || trimmed.StartsWith("//", StringComparison.Ordinal))
            return line;

        var code = raw;
        var cmt = CfgText.FindCommentStart(raw);
        if (cmt >= 0)
        {
            line.TrailingComment = raw[cmt..];
            code = raw[..cmt];
        }

        code = code.Trim();
        if (code.Length == 0) return line;

        // Split into the first token and the remainder.
        var sp = code.IndexOfAny(new[] { ' ', '\t' });
        if (sp <= 0) return line; // a bare command with no argument, e.g. "clear" — preserve

        var cvar = code[..sp].Trim();
        var value = code[(sp + 1)..].Trim();

        if (cvar.Length == 0 || NotCvars.Contains(cvar) ||
            cvar.StartsWith('+') || cvar.StartsWith('-'))
            return line;

        line.Cvar = cvar;
        line.Value = Unquote(value);
        return line;
    }

    private static string Unquote(string v) =>
        v.Length >= 2 && v[0] == '"' && v[^1] == '"' ? v[1..^1] : v;

    public bool Has(string cvar) => FindLine(cvar) is not null;

    public string? Get(string cvar) => FindLine(cvar)?.Value;

    /// <summary>Finds the last assignment of <paramref name="cvar"/> (last value wins in-engine).</summary>
    private Line? FindLine(string cvar)
    {
        for (var i = _lines.Count - 1; i >= 0; i--)
        {
            var l = _lines[i];
            if (l.IsAssignment &&
                string.Equals(l.Cvar, cvar, StringComparison.OrdinalIgnoreCase))
                return l;
        }

        return null;
    }

    public void Set(string cvar, string value)
    {
        var quoted = NeedsQuotes(value) ? $"\"{value}\"" : value;
        var existing = FindLine(cvar);
        if (existing is not null)
        {
            existing.Value = value;
            existing.Raw = Compose(existing.Cvar!, quoted, existing.TrailingComment);
            return;
        }

        _lines.Add(new Line
        {
            Cvar = cvar,
            Value = value,
            Raw = Compose(cvar, quoted, null),
        });
    }

    /// <summary>Removes every assignment of the cvar, so it is truly unset.</summary>
    public bool Remove(string cvar)
    {
        var removed = false;
        for (var i = _lines.Count - 1; i >= 0; i--)
        {
            var l = _lines[i];
            if (l.IsAssignment &&
                string.Equals(l.Cvar, cvar, StringComparison.OrdinalIgnoreCase))
            {
                _lines.RemoveAt(i);
                removed = true;
            }
        }

        return removed;
    }

    /// <summary>Appends a raw line with no interpretation (used for comments and headers).</summary>
    public void AppendRaw(string raw) => _lines.Add(ParseLine(raw));

    private static bool NeedsQuotes(string v) =>
        v.Length == 0 || v.Any(char.IsWhiteSpace);

    private static string Compose(string cvar, string value, string? trailingComment) =>
        trailingComment is null ? $"{cvar} {value}" : $"{cvar} {value} {trailingComment}";

    public string Render()
    {
        var sb = new StringBuilder();
        for (var i = 0; i < _lines.Count; i++)
        {
            sb.Append(_lines[i].Raw);
            if (i < _lines.Count - 1 || _finalNewline) sb.Append(_newline);
        }

        return sb.ToString();
    }
}
