using TF2Configurator.Models;
using TF2Configurator.Services;
using Xunit;

namespace TF2Configurator.Tests;

public sealed class PresetCatalogTests
{
    private static PresetCatalog LoadPresets() =>
        PresetCatalog.Parse(
            CatalogService.ReadEmbedded("preset_modules.json"),
            CatalogService.ReadEmbedded("module_values.json"));

    [Fact]
    public void Embedded_preset_data_loads_and_covers_presets()
    {
        var presets = LoadPresets();
        foreach (var preset in PresetCatalog.PresetNames)
            Assert.True(presets.HasPreset(preset), $"preset '{preset}' should have entries");
    }

    [Fact]
    public void Preset_mapping_matches_known_module_levels()
    {
        var presets = LoadPresets();
        var catalog = ModuleCatalog.Parse(CatalogService.ReadEmbedded("modules.json"));

        foreach (var (module, value) in presets.EntriesFor("medium"))
        {
            var def = catalog.Get(module);
            Assert.NotNull(def); // every preset module must exist in the catalog
            if (value.Length > 0)
                Assert.True(def!.HasValue(value),
                    $"'{module}' preset medium maps to '{value}' which is not a valid level");
        }
    }

    [Fact]
    public void Empty_value_means_unset()
    {
        var presets = LoadPresets();
        // The 'custom' preset clears most modules back to the preset default.
        var cleared = presets.EntriesFor("custom").Where(e => e.Value.Length == 0).ToList();
        Assert.NotEmpty(cleared);
    }

    [Fact]
    public void Modules_the_preset_does_not_mention_are_untouched()
    {
        var presets = LoadPresets();
        var medium = presets.EntriesFor("medium").Select(e => e.Module).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain(medium, m => m == "bandwidth"); // bandwidth has no preset levels
    }

    [Fact]
    public void Details_are_available_for_mapped_modules()
    {
        var presets = LoadPresets();
        Assert.NotEmpty(presets.DetailsFor("post_processing"));
        Assert.NotEmpty(presets.DetailsFor("shadows"));
        Assert.Empty(presets.DetailsFor("fpscap"));
    }

    [Fact]
    public void Details_levels_carry_cvars()
    {
        var presets = LoadPresets();
        var details = presets.DetailsFor("shadows").Single();
        Assert.Contains(details.Levels.Keys, l => l == "off");
        Assert.Contains("r_shadows", details.Levels["off"].Keys);
    }

    [Fact]
    public void Malformed_root_throws()
    {
        Assert.Throws<InvalidDataException>(() => PresetCatalog.Parse("[1]", "{}"));
        Assert.Throws<InvalidDataException>(() => PresetCatalog.Parse("{}", "[1]"));
    }
}
