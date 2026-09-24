using TF2Configurator.Models;
using TF2Configurator.Services;

namespace TF2Configurator.SmokeTest;

/// <summary>
/// Read-only verification harness. Runs the real parsers and validator against a real TF2
/// install and prints what they do, so the round-trip guarantees get checked instead of assumed.
/// Nothing here writes to the game folder.
/// </summary>
internal static class Program
{
    private static int _failures;

    private static int Main(string[] args)
    {
        // Optional explicit game root; otherwise the same detection the app uses.
        var root = args.Length > 0 ? args[0] : null;

        Console.OutputEncoding = System.Text.Encoding.UTF8;

        Section("Catalog");
        var service = new CatalogService();
        var catalog = service.Load();
        Console.WriteLine($"source        : {service.Source}");
        Console.WriteLine($"modules       : {catalog.ModuleCount} in {catalog.Categories.Count} categories");
        foreach (var c in catalog.Categories)
            Console.WriteLine($"  {c.Key,-12} {c.Label,-24} {c.Modules.Count,3} modules");
        Check("catalog is non-empty", catalog.ModuleCount > 0);

        // These run on strings, not the install, so fidelity is checked against every shape a
        // cfg can arrive in — not just the one this machine happens to have.
        Section("Round-trip fidelity (synthetic)");
        RoundTrip("CRLF, final newline", "shadows=off\r\neffects=low\r\n");
        RoundTrip("CRLF, no final newline", "shadows=off\r\neffects=low");
        RoundTrip("LF, final newline", "shadows=off\neffects=low\n");
        RoundTrip("LF, no final newline", "shadows=off\neffects=low");
        RoundTrip("CR only", "shadows=off\reffects=low\r");
        RoundTrip("comments and blank lines", "// header\r\n\r\nshadows=off\r\n\r\n// tail\r\n");
        RoundTrip("trailing comment", "shadows=off // was high\r\n");
        RoundTrip("odd spacing", "  shadows   =   off  \r\n");
        RoundTrip("duplicate key", "shadows=off\r\nshadows=high\r\n");
        RoundTrip("consecutive blank lines", "shadows=off\r\n\r\n\r\n\r\neffects=low\r\n");
        RoundTrip("empty file", "");
        RoundTrip("only a newline", "\r\n");

        Section("Edit fidelity (synthetic)");
        EditKeeps("LF file stays LF when a value changes",
            "shadows=off\neffects=low\n", "shadows", "high", "shadows=high\neffects=low\n");
        EditKeeps("no final newline is not invented",
            "shadows=off\neffects=low", "effects", "high", "shadows=off\neffects=high");
        EditKeeps("appended line uses the file's own terminator",
            "shadows=off\neffects=low", "water", "low", "shadows=off\neffects=low\nwater=low");
        EditKeeps("trailing comment survives a value change",
            "shadows=off // keep me\r\n", "shadows", "high", "shadows=high // keep me\r\n");
        EditKeeps("a new file gets a sane final newline",
            "", "shadows", "off", "shadows=off\r\n");

        Section("Command cfg specifics");
        var cmd = CommandCfg.Parse(
            "bind \"f\" \"+attack\"\r\nalias foo \"bar baz\"\r\ncl_interp 0.0152\r\nname \"Some Player\"\r\n");
        Check("bind is not mistaken for a cvar", cmd.Get("bind") is null);
        Check("alias is not mistaken for a cvar", cmd.Get("alias") is null);
        Check("plain cvar value is read", cmd.Get("cl_interp") == "0.0152");
        Check("quoted value is unquoted on read", cmd.Get("name") == "Some Player");

        cmd.Set("name", "Other Player");
        var cmdText = cmd.Render();
        Check("value containing a space is requoted on write", cmdText.Contains("name \"Other Player\""));
        Check("bind line survives an unrelated edit", cmdText.Contains("bind \"f\" \"+attack\""));
        Check("alias line survives an unrelated edit", cmdText.Contains("alias foo \"bar baz\""));

        Section("Duplicate keys and quoted comments");
        var dup = KeyValueCfg.Parse("shadows=off\r\nshadows=high\r\n");
        Check("duplicate key reads the effective (last) value", dup.Get("shadows") == "high");
        dup.Set("shadows", "medium");
        Check("set edits the last duplicate, not the dead first one",
            dup.Render() == "shadows=off\r\nshadows=medium\r\n");
        dup.Remove("shadows");
        Check("remove drops every duplicate so the key is truly gone", dup.Render() == "");

        var dupCmd = CommandCfg.Parse("cl_interp 0.0152\r\ncl_interp 0.03\r\n");
        Check("command duplicate reads the effective value", dupCmd.Get("cl_interp") == "0.03");
        dupCmd.Set("cl_interp", "0.02");
        Check("command set edits the effective duplicate",
            dupCmd.Render() == "cl_interp 0.0152\r\ncl_interp 0.02\r\n");

        var quoted = CommandCfg.Parse("name \"Some // Player\"\r\n");
        Check("// inside a quoted value is not treated as a comment",
            quoted.Get("name") == "Some // Player" && quoted.Render() == "name \"Some // Player\"\r\n");

        var kvQuoted = KeyValueCfg.Parse("download=\"a // b\"\r\n");
        Check("key=value quoted // is preserved", kvQuoted.Get("download") == "\"a // b\""
            && kvQuoted.Render() == "download=\"a // b\"\r\n");

        Section("Validator suggestions");
        var typo = ConfigValidator.Validate(
            KeyValueCfg.Parse("shadows=meduim\r\n"), catalog);
        Check("a typo'd level gets a fuzzy suggestion",
            typo.Count == 1 && typo[0].SuggestedValue == "medium");

        var stale = ConfigValidator.Validate(
            KeyValueCfg.Parse("post_processing=high\r\n"), catalog);
        Check("post_processing=high suggests the renamed level",
            stale.Count == 1 && stale[0].SuggestedValue == "calm");

        Section("Paths");
        var paths = TF2Paths.Detect(root);
        Console.WriteLine($"game root     : {paths.GameRoot}");
        Console.WriteLine($"detected via  : {paths.DetectionSource}");
        Console.WriteLine($"overrides     : {paths.OverridesDir}");
        Check("install is valid", paths.IsValid);
        if (!paths.IsValid)
            Console.WriteLine("  (no TF2 install found — install-dependent sections are skipped)");

        var status = paths.GetMastercomfigStatus();
        Console.WriteLine($"mastercomfig  : {status.Summary}");
        Console.WriteLine($"  base vpk    : {status.HasBase}");
        Console.WriteLine($"  presets     : {(status.Presets.Count == 0 ? "(none)" : string.Join(", ", status.Presets))}");
        Console.WriteLine($"  addons      : {(status.Addons.Count == 0 ? "(none)" : string.Join(", ", status.Addons))}");

        if (!paths.IsValid) return Done();

        Section("modules.cfg round-trip");
        var raw = SafeWriter.Read(paths.ModulesCfg);
        Console.WriteLine($"file          : {paths.ModulesCfg}");
        Console.WriteLine($"bytes         : {raw.Length}");
        Console.WriteLine($"lines         : {CountLines(raw)}");
        Console.WriteLine($"blank lines   : {CountBlank(raw)}");

        var cfg = KeyValueCfg.Parse(raw);
        var rendered = cfg.Render();
        Check("untouched parse+render is byte-identical", rendered == raw);
        if (rendered != raw) ShowFirstDifference(raw, rendered);

        Check("blank lines survive the round-trip", CountBlank(rendered) == CountBlank(raw));

        Section("modules.cfg contents");
        var assigned = cfg.Entries.ToList();
        Console.WriteLine($"assignments   : {assigned.Count}");
        foreach (var (key, value) in assigned)
        {
            var module = catalog.Get(key);
            var known = module is not null;
            var valid = known && module!.HasValue(value);
            var mark = !known ? "unknown module" : valid ? "ok" : "INVALID LEVEL";
            Console.WriteLine($"  {key,-18} = {value,-12} {mark}");
        }

        Section("Validator");
        var issues = ConfigValidator.Validate(cfg, catalog);
        Console.WriteLine($"issues        : {issues.Count}");
        foreach (var issue in issues)
        {
            Console.WriteLine($"  [{issue.Kind}] {issue.Description}");
            Console.WriteLine($"      → {issue.Resolution}");
        }

        Check("legacy texture_filter=bilinear is reported",
            !string.Equals(cfg.Get("texture_filter"), "bilinear", StringComparison.OrdinalIgnoreCase)
            || issues.Any(i => i.Key.Equals("texture_filter", StringComparison.OrdinalIgnoreCase)));

        Check("legacy hud_avatars=on is reported",
            !string.Equals(cfg.Get("hud_avatars"), "on", StringComparison.OrdinalIgnoreCase)
            || issues.Any(i => i.Key.Equals("hud_avatars", StringComparison.OrdinalIgnoreCase)));

        Section("Surgical edit behaviour");

        // Changing one value must touch exactly one line.
        var changeKey = assigned.Count > 0 ? assigned[0].Key : null;
        if (changeKey is not null)
        {
            var edited = KeyValueCfg.Parse(raw);
            edited.Set(changeKey, "SENTINEL");
            var diff = DifferingLines(raw, edited.Render());
            Console.WriteLine($"set {changeKey}=SENTINEL → {diff} line(s) differ");
            Check("changing one value edits exactly one line", diff == 1);
        }

        // Adding an absent module must append exactly one line.
        var absent = catalog.AllModules.FirstOrDefault(m => cfg.Get(m.Name) is null);
        if (absent is not null)
        {
            var added = KeyValueCfg.Parse(raw);
            added.Set(absent.Name, absent.Values.Count > 0 ? absent.Values[0].Value : "low");
            var delta = CountLines(added.Render()) - CountLines(raw);
            Console.WriteLine($"add {absent.Name} → line count {delta:+0;-0;0}");
            Check("adding a module appends one line", delta == 1);
        }

        // Removing a present module must drop exactly one line and keep the rest intact.
        if (changeKey is not null)
        {
            var removed = KeyValueCfg.Parse(raw);
            removed.Remove(changeKey);
            var text = removed.Render();
            var delta = CountLines(raw) - CountLines(text);
            Console.WriteLine($"remove {changeKey} → line count -{delta}");
            Check("removing a module drops one line", delta == 1);
            Check("removed key is really gone", KeyValueCfg.Parse(text).Get(changeKey) is null);
            Check("blank lines still survive after a removal", CountBlank(text) == CountBlank(raw));
        }

        Section("autoexec.cfg");
        var autoRaw = SafeWriter.Read(paths.AutoexecCfg);
        Console.WriteLine($"file          : {paths.AutoexecCfg}");
        Console.WriteLine($"bytes         : {autoRaw.Length}");
        var auto = CommandCfg.Parse(autoRaw);
        var autoRendered = auto.Render();
        Check("autoexec parse+render is byte-identical", autoRendered == autoRaw);
        if (autoRendered != autoRaw) ShowFirstDifference(autoRaw, autoRendered);

        var recognised = 0;
        var conflicts = new List<string>();
        foreach (var def in CvarCatalog.Autoexec)
        {
            var value = auto.Get(def.Name);
            if (value is null) continue;

            recognised++;
            var note = def.ManagedByModule is null ? "" : $"  ⚠ overrides module '{def.ManagedByModule}'";
            Console.WriteLine($"  {def.Name,-24} = {value,-10}{note}");
            if (def.ManagedByModule is not null)
                conflicts.Add($"{def.Name} → {def.ManagedByModule}");
        }

        Console.WriteLine($"recognised    : {recognised} of {CvarCatalog.Autoexec.Length} known cvars");
        Console.WriteLine($"module clashes: {(conflicts.Count == 0 ? "(none)" : string.Join(", ", conflicts))}");

        Section("Class configs");
        foreach (var (file, display) in TF2Paths.Classes)
        {
            var path = paths.ClassCfg(file);
            var exists = File.Exists(path);
            Console.WriteLine($"  {display,-10} {(exists ? $"{new FileInfo(path).Length,6} bytes" : "     (none)")}");
        }

        return Done();
    }

    // -------------------------------------------------------------------------------------------

    private static void Section(string title)
    {
        Console.WriteLine();
        Console.WriteLine("== " + title + " " + new string('=', Math.Max(0, 66 - title.Length)));
    }

    private static void Check(string what, bool ok)
    {
        if (!ok) _failures++;
        Console.WriteLine($"[{(ok ? " OK " : "FAIL")}] {what}");
    }

    /// <summary>
    /// Parse-then-render with no edits must be the identity function, for both document types and
    /// whatever line endings the file arrived with.
    /// </summary>
    private static void RoundTrip(string what, string text)
    {
        var kv = KeyValueCfg.Parse(text).Render();
        var cc = CommandCfg.Parse(text).Render();

        Check($"modules.cfg · {what}", kv == text);
        if (kv != text) Console.WriteLine($"       {Show(text)}  ->  {Show(kv)}");

        Check($"command cfg · {what}", cc == text);
        if (cc != text) Console.WriteLine($"       {Show(text)}  ->  {Show(cc)}");
    }

    /// <summary>One <see cref="KeyValueCfg.Set"/> call must produce exactly the expected bytes.</summary>
    private static void EditKeeps(string what, string before, string key, string value, string expected)
    {
        var doc = KeyValueCfg.Parse(before);
        doc.Set(key, value);
        var after = doc.Render();

        Check(what, after == expected);
        if (after != expected)
            Console.WriteLine($"       expected {Show(expected)}\r\n       actual   {Show(after)}");
    }

    /// <summary>Renders line endings visibly, so a failure message says what actually differs.</summary>
    private static string Show(string text) =>
        "\"" + text.Replace("\r", "\\r").Replace("\n", "\\n") + "\"";

    private static int Done()
    {
        Console.WriteLine();
        Console.WriteLine(_failures == 0
            ? "All checks passed."
            : $"{_failures} check(s) FAILED.");
        return _failures == 0 ? 0 : 1;
    }

    private static int CountLines(string text) =>
        text.Length == 0 ? 0 : text.Replace("\r\n", "\n").Split('\n').Length;

    private static int CountBlank(string text) =>
        text.Replace("\r\n", "\n").Split('\n').Count(l => l.Trim().Length == 0);

    private static int DifferingLines(string a, string b)
    {
        var left = a.Replace("\r\n", "\n").Split('\n');
        var right = b.Replace("\r\n", "\n").Split('\n');
        var count = Math.Abs(left.Length - right.Length);

        for (var i = 0; i < Math.Min(left.Length, right.Length); i++)
            if (left[i] != right[i]) count++;

        return count;
    }

    private static void ShowFirstDifference(string expected, string actual)
    {
        var left = expected.Replace("\r\n", "\n").Split('\n');
        var right = actual.Replace("\r\n", "\n").Split('\n');

        for (var i = 0; i < Math.Max(left.Length, right.Length); i++)
        {
            var l = i < left.Length ? left[i] : "(missing)";
            var r = i < right.Length ? right[i] : "(missing)";
            if (l == r) continue;

            Console.WriteLine($"       first difference at line {i + 1}:");
            Console.WriteLine($"         on disk : \"{l}\"");
            Console.WriteLine($"         rendered: \"{r}\"");
            return;
        }
    }
}
