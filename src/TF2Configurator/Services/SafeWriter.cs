using TF2Configurator.Models;

namespace TF2Configurator.Services;

/// <summary>Writes config files safely: creates the folder, and keeps a timestamped backup.</summary>
public static class SafeWriter
{
    public static string BackupRoot =>
        Path.Combine(CatalogService.CacheDir, "backups");

    /// <summary>
    /// Writes <paramref name="contents"/> to <paramref name="path"/>, first copying any existing
    /// file into a timestamped backup folder. Returns the backup path, or null if there was
    /// nothing to back up.
    /// </summary>
    public static string? Write(string path, string contents)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        string? backup = null;
        if (File.Exists(path))
        {
            // Skip the backup when nothing actually changed, to avoid piling up identical copies.
            try
            {
                if (File.ReadAllText(path) == contents) return null;
            }
            catch
            {
                // If we cannot read it, fall through and back it up by copying.
            }

            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var folder = Path.Combine(BackupRoot, stamp);
            Directory.CreateDirectory(folder);
            backup = Path.Combine(folder, Path.GetFileName(path));
            File.Copy(path, backup, overwrite: true);
        }

        File.WriteAllText(path, contents);
        PruneBackups();
        return backup;
    }

    /// <summary>
    /// Keeps the backup store bounded: drops folders older than 30 days, then keeps only the
    /// 60 most recent. Best-effort — a pruning failure must never fail a save.
    /// </summary>
    private static void PruneBackups()
    {
        try
        {
            if (!Directory.Exists(BackupRoot)) return;

            var cutoff = DateTime.Now.AddDays(-30);
            foreach (var d in Snapshot().Where(d => d.LastWriteTime < cutoff))
                TryDelete(d);

            foreach (var d in Snapshot().Skip(60))
                TryDelete(d);
        }
        catch
        {
            // Non-fatal.
        }
    }

    private static List<DirectoryInfo> Snapshot()
    {
        try
        {
            return Directory.GetDirectories(BackupRoot)
                .Select(p => new DirectoryInfo(p))
                .OrderByDescending(d => d.Name, StringComparer.Ordinal) // yyyyMMdd-HHmmss sorts as time
                .ToList();
        }
        catch
        {
            return new List<DirectoryInfo>();
        }
    }

    private static void TryDelete(DirectoryInfo dir)
    {
        try
        {
            if (dir.Exists) dir.Delete(recursive: true);
        }
        catch
        {
            // Locked or otherwise un-deletable — leave it.
        }
    }

    public static string Read(string path) =>
        File.Exists(path) ? File.ReadAllText(path) : "";
}

public enum IssueKind
{
    /// <summary>The module name is not in the catalog at all.</summary>
    UnknownModule,

    /// <summary>The module exists but the assigned level is not one of its valid values.</summary>
    InvalidValue,

    /// <summary>Same key assigned more than once; TF2 uses the last one.</summary>
    Duplicate,
}

public sealed class ConfigIssue
{
    public required IssueKind Kind { get; init; }
    public required string Key { get; init; }
    public required string Value { get; init; }

    /// <summary>A confidently-known replacement, when one exists. Never applied automatically.</summary>
    public string? SuggestedValue { get; init; }

    public string Description => Kind switch
    {
        IssueKind.UnknownModule =>
            $"'{Key}' is not a module in the current mastercomfig catalog.",
        IssueKind.InvalidValue when SuggestedValue is not null =>
            $"'{Key}={Value}' is not a valid level. '{SuggestedValue}' is the closest current equivalent.",
        IssueKind.InvalidValue =>
            $"'{Value}' is not a valid level for '{Key}'.",
        IssueKind.Duplicate =>
            $"'{Key}' is set more than once; TF2 will use the last value.",
        _ => Key,
    };

    /// <summary>Kept-as-is reassurance shown next to every issue.</summary>
    public string Resolution => SuggestedValue is not null
        ? $"Kept as-is. Suggested fix: {Key}={SuggestedValue}"
        : "Kept as-is — nothing was changed or removed.";
}

/// <summary>
/// Checks a modules.cfg against the catalog and reports problems without changing anything.
/// mastercomfig retires level names between releases, and TF2 silently ignores unknown values,
/// so an old config can quietly stop applying. Pointing that out beats rewriting the file.
/// </summary>
public static class ConfigValidator
{
    /// <summary>
    /// Levels mastercomfig removed, mapped to their current equivalents. Kept small on purpose:
    /// only cases where the old level clearly maps to a new one. Anything else is reported as
    /// invalid with no guess. Values not listed here fall through to
    /// <see cref="ClosestValue"/> for a typo-tolerant suggestion.
    /// </summary>
    private static readonly Dictionary<(string Module, string OldValue), string> KnownRenames =
        new(new ModuleValueComparer())
        {
            // Older mastercomfig offered blocky/bilinear/trilinear/aniso*; bilinear was dropped.
            [("texture_filter", "bilinear")] = "trilinear",

            // hud_avatars became a three-way choice (off/everyone/friends) from a simple on/off.
            [("hud_avatars", "on")] = "everyone",

            // post_processing levels were renamed; the 'high' preset maps to 'calm' today.
            [("post_processing", "high")] = "calm",
        };

    public static List<ConfigIssue> Validate(KeyValueCfg cfg, ModuleCatalog catalog)
    {
        var issues = new List<ConfigIssue>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (key, value) in cfg.Entries)
        {
            if (!seen.Add(key))
            {
                issues.Add(new ConfigIssue
                {
                    Kind = IssueKind.Duplicate,
                    Key = key,
                    Value = value,
                });
                continue;
            }

            var module = catalog.Get(key);
            if (module is null)
            {
                issues.Add(new ConfigIssue
                {
                    Kind = IssueKind.UnknownModule,
                    Key = key,
                    Value = value,
                });
                continue;
            }

            if (!module.HasValue(value))
            {
                KnownRenames.TryGetValue((key, value), out var suggestion);
                suggestion ??= ClosestValue(module, value);
                issues.Add(new ConfigIssue
                {
                    Kind = IssueKind.InvalidValue,
                    Key = key,
                    Value = value,
                    SuggestedValue = suggestion,
                });
            }
        }

        return issues;
    }

    /// <summary>
    /// The catalog level closest to a misspelled value, if close enough to be sure. A threshold
    /// of 2 edits catches typos like <c>meduim</c> → <c>medium</c> without jumping to something
    /// unrelated.
    /// </summary>
    private static string? ClosestValue(ModuleDef module, string value)
    {
        var target = value.ToLowerInvariant();
        string? best = null;
        var bestDist = int.MaxValue;

        foreach (var candidate in module.Values)
        {
            var dist = Levenshtein(target, candidate.Value.ToLowerInvariant());
            if (dist < bestDist)
            {
                bestDist = dist;
                best = candidate.Value;
            }
        }

        return best is not null && bestDist <= 2 ? best : null;
    }

    private static int Levenshtein(string a, string b)
    {
        if (a.Length == 0) return b.Length;
        if (b.Length == 0) return a.Length;

        var prev = new int[b.Length + 1];
        var curr = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++) prev[j] = j;

        for (var i = 1; i <= a.Length; i++)
        {
            curr[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                curr[j] = Math.Min(
                    Math.Min(curr[j - 1] + 1, prev[j] + 1),
                    prev[j - 1] + cost);
            }

            (prev, curr) = (curr, prev);
        }

        return prev[b.Length];
    }

    private sealed class ModuleValueComparer : IEqualityComparer<(string Module, string OldValue)>
    {
        public bool Equals((string Module, string OldValue) a, (string Module, string OldValue) b) =>
            string.Equals(a.Module, b.Module, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(a.OldValue, b.OldValue, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string Module, string OldValue) x) =>
            HashCode.Combine(
                x.Module.ToLowerInvariant(),
                x.OldValue.ToLowerInvariant());
    }
}
