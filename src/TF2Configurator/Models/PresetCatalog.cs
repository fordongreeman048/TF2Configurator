using System.Globalization;
using System.Text.Json;

namespace TF2Configurator.Models;

/// <summary>
/// Parsed mastercomfig preset data. Drives two features:
/// <list type="bullet">
/// <item><c>preset_modules.json</c> — the level each preset (destitute/low/medium/high/ultra/custom)
/// chooses per module; an empty value means the module is left unset.</item>
/// <item><c>module_values.json</c> — the console variables each level sets, shown in the module
/// details dialog so a picker choice is not a black box.</item>
/// </list>
/// </summary>
public sealed class PresetCatalog
{
    /// <summary>The preset names mastercomfig defines, in display order.</summary>
    public static readonly string[] PresetNames = { "destitute", "low", "medium", "high", "ultra", "custom" };

    private readonly Dictionary<string, Dictionary<string, string>> _presets =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, Dictionary<string, Dictionary<string, string>>> _sections =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Maps mastercomfig's internal data sections to the modules in modules.json, so the details
    /// dialog can show cvars per module. Some sections cover several modules (graphics.world_detail
    /// is both 3dsky and props); a few (like sound.voice) are empty in the source data and are
    /// intentionally not mapped.
    /// </summary>
    private static readonly Dictionary<string, string> ModuleToSection =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["lod"] = "graphics.model_quality",
            ["lighting"] = "graphics.lighting",
            ["shading"] = "graphics.shading",
            ["phong"] = "graphics.shading",
            ["shadows"] = "graphics.shadows",
            ["effects"] = "graphics.effects",
            ["tracers"] = "graphics.particles",
            ["pyrovision"] = "post_processing.pyrovision",
            ["water"] = "graphics.water",
            ["post_processing"] = "post_processing.bloom",
            ["color_filter"] = "post_processing.color_filter",
            ["characters"] = "graphics.characters",
            ["decals"] = "graphics.decals",
            ["gibs"] = "graphics.gibs",
            ["props"] = "graphics.world_detail",
            ["3dsky"] = "graphics.world_detail",
            ["ragdolls"] = "graphics.ragdolls",
            ["ropes"] = "graphics.ropes",
            ["texture_quality"] = "graphics.textures",
            ["texture_filter"] = "graphics.textures",
            ["sound"] = "sound.quality",
            ["voice_chat"] = "sound.voice",
        };

    /// <summary>True when <paramref name="preset"/> has any module entries in the source data.</summary>
    public bool HasPreset(string preset) =>
        _presets.Values.Any(m => m.ContainsKey(preset));

    /// <summary>The level a preset chooses for a module, or null when the preset leaves it alone.</summary>
    public string? Get(string module, string preset) =>
        _presets.TryGetValue(module, out var map) && map.TryGetValue(preset, out var value)
            ? value
            : null;

    /// <summary>
    /// All module/value pairs a preset implies. An empty value means "unset this module so the
    /// preset default applies again". Only modules with an explicit entry for the preset are
    /// returned — modules the preset does not mention are not the caller's business to touch.
    /// </summary>
    public IEnumerable<(string Module, string Value)> EntriesFor(string preset)
    {
        foreach (var (module, map) in _presets)
        {
            if (!map.TryGetValue(preset, out var value)) continue;
            yield return (module, value);
        }
    }

    public sealed record ModuleDetails(string Section, Dictionary<string, Dictionary<string, string>> Levels);

    /// <summary>
    /// The section(s) of console variables behind a module's levels, for the details dialog.
    /// Empty when the bundled data has nothing for this module.
    /// </summary>
    public IEnumerable<ModuleDetails> DetailsFor(string module)
    {
        if (!ModuleToSection.TryGetValue(module, out var section) ||
            !_sections.TryGetValue(section, out var levels) || levels.Count == 0)
            yield break;

        yield return new ModuleDetails(section, levels);
    }

    public static PresetCatalog Parse(string presetJson, string valuesJson)
    {
        var catalog = new PresetCatalog();

        // preset_modules.json: { "module": { "preset": "level" } }
        using (var doc = JsonDocument.Parse(presetJson))
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("Preset catalog root is not a JSON object.");

            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.Value.ValueKind != JsonValueKind.Object) continue;
                var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                foreach (var level in prop.Value.EnumerateObject())
                {
                    var value = level.Value.ValueKind switch
                    {
                        JsonValueKind.String => level.Value.GetString() ?? "",
                        JsonValueKind.Number => ScalarNumber(level.Value),
                        _ => "",
                    };
                    map[level.Name] = value;
                }

                if (map.Count > 0) catalog._presets[prop.Name] = map;
            }
        }

        // module_values.json: { "section": { "level": { "cvar": value } } }
        using (var doc = JsonDocument.Parse(valuesJson))
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("Module values root is not a JSON object.");

            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.Value.ValueKind != JsonValueKind.Object) continue;
                var levels = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

                foreach (var level in prop.Value.EnumerateObject())
                {
                    if (level.Value.ValueKind != JsonValueKind.Object) continue;
                    var cvars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                    foreach (var cvar in level.Value.EnumerateObject())
                        cvars[cvar.Name] = Scalar(cvar.Value);

                    levels[level.Name] = cvars;
                }

                if (levels.Count > 0) catalog._sections[prop.Name] = levels;
            }
        }

        if (catalog._presets.Count == 0 && catalog._sections.Count == 0)
            throw new InvalidDataException("Preset data contained no usable entries.");

        return catalog;
    }

    private static string Scalar(JsonElement e) =>
        e.ValueKind switch
        {
            JsonValueKind.String => e.GetString() ?? "",
            JsonValueKind.Number => ScalarNumber(e),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => "",
        };

    private static string ScalarNumber(JsonElement e) =>
        e.TryGetInt64(out var whole)
            ? whole.ToString(CultureInfo.InvariantCulture)
            : e.GetDouble().ToString(CultureInfo.InvariantCulture);
}
