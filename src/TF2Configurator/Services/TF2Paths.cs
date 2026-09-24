using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace TF2Configurator.Services;

/// <summary>
/// Finds the Team Fortress 2 install and the config folders this app writes to.
/// Order: user override, then Steam's registry keys plus <c>libraryfolders.vdf</c>
/// (so extra library drives are found), then a few common paths.
/// </summary>
public sealed class TF2Paths
{
    /// <summary>Root of the install, i.e. the folder containing <c>tf\</c> and <c>hl2.exe</c>.</summary>
    public string? GameRoot { get; private set; }

    /// <summary>How <see cref="GameRoot"/> was found, for display in the UI.</summary>
    public string DetectionSource { get; private set; } = "not detected";

    public bool IsValid => GameRoot is not null && Directory.Exists(TfDir);

    public string TfDir => Path.Combine(GameRoot ?? "", "tf");
    public string CfgDir => Path.Combine(TfDir, "cfg");

    /// <summary>mastercomfig reads user configs from <c>tf\cfg\overrides\</c>.</summary>
    public string OverridesDir => Path.Combine(CfgDir, "overrides");

    public string CustomDir => Path.Combine(TfDir, "custom");

    public string ModulesCfg => Path.Combine(OverridesDir, "modules.cfg");
    public string AutoexecCfg => Path.Combine(OverridesDir, "autoexec.cfg");

    public string ClassCfg(string className) =>
        Path.Combine(OverridesDir, className + ".cfg");

    /// <summary>The nine class config filenames TF2 actually execs (note: "heavyweapons").</summary>
    public static readonly (string File, string Display)[] Classes =
    {
        ("scout",        "Scout"),
        ("soldier",      "Soldier"),
        ("pyro",         "Pyro"),
        ("demoman",      "Demoman"),
        ("heavyweapons", "Heavy"),
        ("engineer",     "Engineer"),
        ("medic",        "Medic"),
        ("sniper",       "Sniper"),
        ("spy",          "Spy"),
    };

    public static TF2Paths Detect(string? userOverride = null)
    {
        var p = new TF2Paths();

        if (!string.IsNullOrWhiteSpace(userOverride))
        {
            var normalized = NormalizeCandidate(userOverride!);
            if (normalized is not null)
            {
                p.GameRoot = normalized;
                p.DetectionSource = "manual override";
                return p;
            }
        }

        foreach (var (root, source) in EnumerateCandidates())
        {
            var normalized = NormalizeCandidate(root);
            if (normalized is not null)
            {
                p.GameRoot = normalized;
                p.DetectionSource = source;
                return p;
            }
        }

        return p;
    }

    /// <summary>
    /// Accepts either the game root or a path pointing at <c>tf\</c> (or deeper) and
    /// returns the game root, or null when the path is not a TF2 install.
    /// </summary>
    private static string? NormalizeCandidate(string path)
    {
        try
        {
            var dir = path.Trim().Trim('"');
            if (dir.Length == 0) return null;

            // Walk up if the user pointed at tf\ or tf\cfg\.
            for (var i = 0; i < 4 && dir.Length > 0; i++)
            {
                if (LooksLikeGameRoot(dir)) return Path.GetFullPath(dir);
                var parent = Path.GetDirectoryName(dir);
                if (string.IsNullOrEmpty(parent) || parent == dir) break;
                dir = parent;
            }
        }
        catch
        {
            // Malformed path — treat as not found.
        }

        return null;
    }

    private static bool LooksLikeGameRoot(string dir) =>
        Directory.Exists(Path.Combine(dir, "tf")) &&
        (File.Exists(Path.Combine(dir, "tf", "gameinfo.txt")) ||
         File.Exists(Path.Combine(dir, "hl2.exe")) ||
         File.Exists(Path.Combine(dir, "tf_win64.exe")) ||
         File.Exists(Path.Combine(dir, "tf.exe")));

    private static IEnumerable<(string Root, string Source)> EnumerateCandidates()
    {
        foreach (var lib in SteamLibraries())
        {
            var candidate = Path.Combine(lib, "steamapps", "common", "Team Fortress 2");
            if (Directory.Exists(candidate))
                yield return (candidate, "Steam library: " + lib);
        }

        foreach (var drive in new[] { "C", "D", "E", "F" })
        {
            yield return ($@"{drive}:\SteamLibrary\steamapps\common\Team Fortress 2", "common path scan");
            yield return ($@"{drive}:\Program Files (x86)\Steam\steamapps\common\Team Fortress 2", "common path scan");
            yield return ($@"{drive}:\Steam\steamapps\common\Team Fortress 2", "common path scan");
        }
    }

    /// <summary>Steam install path from the registry, plus every library folder it knows about.</summary>
    public static List<string> SteamLibraries()
    {
        var results = new List<string>();
        var steam = SteamInstallPath();
        if (steam is null) return results;

        results.Add(steam);

        try
        {
            var vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
            if (File.Exists(vdf))
            {
                var text = File.ReadAllText(vdf);

                // Current format has "path" "<dir>"; older ones map an index straight to the dir.
                foreach (Match m in Regex.Matches(text, "\"path\"\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase))
                    AddLibrary(results, m.Groups[1].Value);

                foreach (Match m in Regex.Matches(text, "^\\s*\"\\d+\"\\s*\"([^\"]+)\"", RegexOptions.Multiline))
                    AddLibrary(results, m.Groups[1].Value);
            }
        }
        catch
        {
            // Unreadable VDF is not fatal; the path scan below still applies.
        }

        return results;
    }

    private static void AddLibrary(List<string> into, string raw)
    {
        var dir = raw.Replace(@"\\", @"\").Trim();
        if (dir.Length > 0 && !into.Contains(dir, StringComparer.OrdinalIgnoreCase))
            into.Add(dir);
    }

    private static string? SteamInstallPath()
    {
        foreach (var (hive, key, value) in new (RegistryKey, string, string)[]
                 {
                     (Registry.CurrentUser,  @"SOFTWARE\Valve\Steam",              "SteamPath"),
                     (Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam",  "InstallPath"),
                     (Registry.LocalMachine, @"SOFTWARE\Valve\Steam",              "InstallPath"),
                 })
        {
            try
            {
                using var k = hive.OpenSubKey(key);
                if (k?.GetValue(value) is string s && s.Length > 0)
                {
                    var dir = s.Replace('/', '\\');
                    if (Directory.Exists(dir)) return dir;
                }
            }
            catch
            {
                // Registry not readable — fall through to the next candidate.
            }
        }

        return null;
    }

    /// <summary>Reports whether mastercomfig is installed, and which pieces are present.</summary>
    public MastercomfigStatus GetMastercomfigStatus()
    {
        var status = new MastercomfigStatus();
        if (!IsValid || !Directory.Exists(CustomDir)) return status;

        try
        {
            foreach (var file in Directory.EnumerateFiles(CustomDir, "*.vpk", SearchOption.AllDirectories))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                if (!name.StartsWith("mastercomfig", StringComparison.OrdinalIgnoreCase)) continue;

                if (name.Contains("-preset", StringComparison.OrdinalIgnoreCase))
                    status.Presets.Add(name);
                else if (name.Contains("-addon-", StringComparison.OrdinalIgnoreCase))
                    status.Addons.Add(name);
                else if (name.Contains("base", StringComparison.OrdinalIgnoreCase))
                    status.HasBase = true;
            }
        }
        catch
        {
            // Enumeration failure leaves the status empty rather than throwing.
        }

        return status;
    }
}

public sealed class MastercomfigStatus
{
    public bool HasBase { get; set; }
    public List<string> Presets { get; } = new();
    public List<string> Addons { get; } = new();

    public bool IsInstalled => HasBase || Presets.Count > 0;

    /// <summary>
    /// mastercomfig needs the base VPK *and* a preset VPK. A base with no preset is a real
    /// misconfiguration worth surfacing, because modules still apply but preset defaults do not.
    /// </summary>
    public string Summary
    {
        get
        {
            if (!IsInstalled) return "mastercomfig not detected in tf\\custom";
            var parts = new List<string>();
            parts.Add(HasBase ? "base ✓" : "base ✗");
            parts.Add(Presets.Count > 0
                ? "preset: " + string.Join(", ", Presets.Select(PrettyPreset))
                : "no preset VPK found");
            if (Addons.Count > 0) parts.Add($"{Addons.Count} addon(s)");
            return string.Join("  |  ", parts);
        }
    }

    private static string PrettyPreset(string vpkName)
    {
        var n = vpkName.Replace("mastercomfig-", "", StringComparison.OrdinalIgnoreCase)
                       .Replace("-preset", "", StringComparison.OrdinalIgnoreCase);
        return n.Length == 0 ? vpkName : n;
    }
}
