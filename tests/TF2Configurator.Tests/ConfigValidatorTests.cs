using TF2Configurator.Models;
using TF2Configurator.Services;
using Xunit;

namespace TF2Configurator.Tests;

public sealed class ConfigValidatorTests
{
    private static ModuleCatalog LoadCatalog() =>
        ModuleCatalog.Parse(CatalogService.ReadEmbedded("modules.json"));

    private static List<ConfigIssue> Validate(string cfgText)
    {
        var catalog = LoadCatalog();
        return ConfigValidator.Validate(KeyValueCfg.Parse(cfgText), catalog);
    }

    [Fact]
    public void Valid_file_produces_no_issues() =>
        Assert.Empty(Validate("shadows=high\r\neffects=ultra\r\n"));

    [Fact]
    public void Values_are_matched_case_insensitively() =>
        Assert.Empty(Validate("shadows=HIGH\r\n"));

    [Fact]
    public void Unknown_module_is_reported_without_a_suggestion()
    {
        var issues = Validate("not_a_module=low\r\n");
        var issue = Assert.Single(issues);
        Assert.Equal(IssueKind.UnknownModule, issue.Kind);
        Assert.Null(issue.SuggestedValue);
    }

    [Fact]
    public void Known_rename_suggests_the_current_level()
    {
        var issues = Validate("texture_filter=bilinear\r\n");
        var issue = Assert.Single(issues);
        Assert.Equal(IssueKind.InvalidValue, issue.Kind);
        Assert.Equal("trilinear", issue.SuggestedValue);
    }

    [Fact]
    public void Renamed_post_processing_level_suggests_calm()
    {
        var issues = Validate("post_processing=high\r\n");
        var issue = Assert.Single(issues);
        Assert.Equal("calm", issue.SuggestedValue);
    }

    [Fact]
    public void Typo_gets_a_fuzzy_suggestion()
    {
        var issues = Validate("shadows=meduim\r\n");
        var issue = Assert.Single(issues);
        Assert.Equal("medium", issue.SuggestedValue);
    }

    [Fact]
    public void Unrelated_word_gets_no_fuzzy_suggestion()
    {
        var issues = Validate("shadows=banana\r\n");
        var issue = Assert.Single(issues);
        Assert.Null(issue.SuggestedValue);
    }

    [Fact]
    public void Duplicate_keys_are_reported()
    {
        var issues = Validate("shadows=off\r\nshadows=high\r\n");
        Assert.Contains(issues, i => i.Kind == IssueKind.Duplicate && i.Key == "shadows");
    }

    [Fact]
    public void Resolution_never_promises_a_change()
    {
        var issues = Validate("texture_filter=bilinear\r\n");
        var issue = Assert.Single(issues);
        Assert.Contains("Kept as-is", issue.Resolution);
        Assert.Contains("texture_filter=trilinear", issue.Resolution);
    }
}
