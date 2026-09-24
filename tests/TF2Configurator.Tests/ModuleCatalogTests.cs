using TF2Configurator.Models;
using TF2Configurator.Services;
using Xunit;

namespace TF2Configurator.Tests;

public sealed class ModuleCatalogTests
{
    private static ModuleCatalog LoadCatalog() =>
        ModuleCatalog.Parse(CatalogService.ReadEmbedded("modules.json"));

    [Fact]
    public void Embedded_catalog_loads_and_is_non_empty()
    {
        var catalog = LoadCatalog();
        Assert.True(catalog.ModuleCount > 0);
        Assert.True(catalog.Categories.Count > 0);
    }

    [Fact]
    public void Every_module_has_a_name_and_at_least_one_value()
    {
        var catalog = LoadCatalog();
        Assert.All(catalog.AllModules, m =>
        {
            Assert.False(string.IsNullOrWhiteSpace(m.Name));
            Assert.NotEmpty(m.Values);
        });
    }

    [Fact]
    public void Category_keys_are_filled_in_and_lookup_is_case_insensitive()
    {
        var catalog = LoadCatalog();
        var module = catalog.AllModules.First();
        Assert.Equal(module.CategoryKey, catalog.Categories
            .First(c => c.Modules.Contains(module)).Key);

        Assert.NotNull(catalog.Get(module.Name.ToUpperInvariant()));
    }

    [Fact]
    public void Slider_levels_written_as_bare_strings_parse_as_values()
    {
        var catalog = LoadCatalog();
        var fpscap = catalog.Get("fpscap");
        Assert.NotNull(fpscap);
        Assert.Contains("240", fpscap.Values.Select(v => v.Value));
        Assert.Contains("unlimited", fpscap.Values.Select(v => v.Value));
    }

    [Fact]
    public void Numeric_and_boolean_levels_are_tolerated()
    {
        var catalog = LoadCatalog();
        Assert.All(catalog.AllModules, m =>
            Assert.All(m.Values, v => Assert.False(string.IsNullOrEmpty(v.Value))));
    }

    [Fact]
    public void Root_that_is_not_an_object_throws()
    {
        Assert.Throws<InvalidDataException>(() => ModuleCatalog.Parse("[1,2,3]"));
    }

    [Fact]
    public void Empty_catalog_throws()
    {
        Assert.Throws<InvalidDataException>(() => ModuleCatalog.Parse("{}"));
    }

    [Fact]
    public void Malformed_entries_are_dropped_instead_of_failing_the_load()
    {
        var json = """
            {
              "graphics": {
                "display": "Graphics",
                "modules": [
                  { "name": "ok", "values": [ { "value": "low" } ] },
                  { "name": "", "values": [ { "value": "low" } ] },
                  { "name": "novalues", "values": [] }
                ]
              }
            }
            """;

        var catalog = ModuleCatalog.Parse(json);
        Assert.Equal(1, catalog.ModuleCount);
        Assert.NotNull(catalog.Get("ok"));
    }

    [Fact]
    public void Tooltip_includes_description_notes_and_cost()
    {
        var json = """
            {
              "graphics": {
                "modules": [
                  {
                    "name": "demo",
                    "description": "A description.",
                    "cpu": 2,
                    "gpu": 3,
                    "notes": [ { "level": "warning", "content": "Be careful." } ],
                    "values": [ { "value": "low" } ]
                  }
                ]
              }
            }
            """;

        var tooltip = ModuleCatalog.Parse(json).Get("demo")!.Tooltip;
        Assert.Contains("A description.", tooltip);
        Assert.Contains("Be careful.", tooltip);
        Assert.Contains("CPU", tooltip);
        Assert.Contains("GPU", tooltip);
    }
}
