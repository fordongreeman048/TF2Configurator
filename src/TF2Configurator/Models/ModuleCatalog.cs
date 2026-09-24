using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TF2Configurator.Models;

/// <summary>One selectable level of a module, e.g. <c>shadows=high</c>.</summary>
[JsonConverter(typeof(ModuleValueConverter))]
public sealed class ModuleValue
{
    [JsonPropertyName("value")] public string Value { get; set; } = "";
    [JsonPropertyName("display")] public string? Display { get; set; }
    [JsonPropertyName("description")] public string? Description { get; set; }

    /// <summary>Text shown in the combo box.</summary>
    public string Label =>
        !string.IsNullOrWhiteSpace(Display) ? Display! : Naming.Prettify(Value);

    /// <summary>Tooltip text; falls back to the display string when there is no description.</summary>
    public string? Detail =>
        !string.IsNullOrWhiteSpace(Description) ? Description
        : (!string.IsNullOrWhiteSpace(Display) ? Display : null);

    public override string ToString() => Label;
}

public sealed class ModuleNote
{
    [JsonPropertyName("level")] public string? Level { get; set; }
    [JsonPropertyName("content")] public string? Content { get; set; }
}

/// <summary>
/// mastercomfig writes a level either as an object — <c>{"value":"low","display":"Low"}</c> — or,
/// for slider modules such as <c>fpscap</c> and <c>bandwidth</c>, as a bare string. Both mean the
/// same thing, so accept either instead of failing on the second form.
/// </summary>
internal sealed class ModuleValueConverter : JsonConverter<ModuleValue>
{
    public override ModuleValue Read(ref Utf8JsonReader reader, Type typeToConvert,
        JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.String:
                return new ModuleValue { Value = reader.GetString() ?? "" };

            // Unquoted numeric levels are valid JSON and mean the same as the quoted form.
            case JsonTokenType.Number:
                return new ModuleValue { Value = Number(ref reader) };

            case JsonTokenType.StartObject:
                return ReadObject(ref reader);

            default:
                throw new JsonException($"Unexpected {reader.TokenType} where a module level was expected.");
        }
    }

    private static ModuleValue ReadObject(ref Utf8JsonReader reader)
    {
        var result = new ModuleValue();

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject) return result;
            if (reader.TokenType != JsonTokenType.PropertyName) continue;

            var name = reader.GetString();
            if (!reader.Read()) break;

            switch (name?.ToLowerInvariant())
            {
                case "value":
                    result.Value = ReadScalar(ref reader) ?? "";
                    break;
                case "display":
                    result.Display = ReadScalar(ref reader);
                    break;
                case "description":
                    result.Description = ReadScalar(ref reader);
                    break;
                default:
                    // Unknown keys are the catalog growing new features, not an error.
                    reader.Skip();
                    break;
            }
        }

        throw new JsonException("Unterminated module level object.");
    }

    private static string? ReadScalar(ref Utf8JsonReader reader)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.String: return reader.GetString();
            case JsonTokenType.Number: return Number(ref reader);
            case JsonTokenType.True: return "true";
            case JsonTokenType.False: return "false";
            case JsonTokenType.Null: return null;
            default:
                reader.Skip();
                return null;
        }
    }

    private static string Number(ref Utf8JsonReader reader) =>
        reader.TryGetInt64(out var whole)
            ? whole.ToString(CultureInfo.InvariantCulture)
            : reader.GetDouble().ToString(CultureInfo.InvariantCulture);

    public override void Write(Utf8JsonWriter writer, ModuleValue value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("value", value.Value);
        if (value.Display is not null) writer.WriteString("display", value.Display);
        if (value.Description is not null) writer.WriteString("description", value.Description);
        writer.WriteEndObject();
    }
}

/// <summary>A mastercomfig module: a named setting with a fixed set of levels.</summary>
public sealed class ModuleDef
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("display")] public string? Display { get; set; }
    [JsonPropertyName("description")] public string? Description { get; set; }

    /// <summary>"switch" or "slider" in the source data; absent means a plain multi-level module.</summary>
    [JsonPropertyName("type")] public string? Type { get; set; }

    /// <summary>Relative CPU cost, 1-3. 0 when unspecified.</summary>
    [JsonPropertyName("cpu")] public int Cpu { get; set; }

    /// <summary>Relative GPU cost, 1-3. 0 when unspecified.</summary>
    [JsonPropertyName("gpu")] public int Gpu { get; set; }

    [JsonPropertyName("notes")] public List<ModuleNote>? Notes { get; set; }
    [JsonPropertyName("tags")] public List<string>? Tags { get; set; }
    [JsonPropertyName("values")] public List<ModuleValue> Values { get; set; } = new();

    /// <summary>Category key this module was found under. Filled in by the catalog loader.</summary>
    [JsonIgnore] public string CategoryKey { get; set; } = "";

    public string Label => !string.IsNullOrWhiteSpace(Display) ? Display! : Naming.Prettify(Name);

    public bool HasValue(string value) =>
        Values.Any(v => string.Equals(v.Value, value, StringComparison.OrdinalIgnoreCase));

    public ModuleValue? Find(string value) =>
        Values.FirstOrDefault(v => string.Equals(v.Value, value, StringComparison.OrdinalIgnoreCase));

    /// <summary>Compact "CPU ••· / GPU •··" style cost string, or empty when unrated.</summary>
    public string CostText
    {
        get
        {
            if (Cpu <= 0 && Gpu <= 0) return "";
            var sb = new StringBuilder();
            if (Cpu > 0) sb.Append("CPU ").Append(Dots(Cpu));
            if (Cpu > 0 && Gpu > 0) sb.Append("  ");
            if (Gpu > 0) sb.Append("GPU ").Append(Dots(Gpu));
            return sb.ToString();
        }
    }

    private static string Dots(int n) => new string('●', Math.Clamp(n, 0, 3))
                                       + new string('·', Math.Clamp(3 - n, 0, 3));

    /// <summary>Full tooltip: description, notes and cost.</summary>
    public string Tooltip
    {
        get
        {
            var sb = new StringBuilder();
            sb.Append(Name);
            if (!string.IsNullOrWhiteSpace(Description)) sb.AppendLine().AppendLine().Append(Description);
            if (Notes is { Count: > 0 })
                foreach (var n in Notes)
                    if (!string.IsNullOrWhiteSpace(n.Content))
                        sb.AppendLine().AppendLine()
                          .Append(string.Equals(n.Level, "warning", StringComparison.OrdinalIgnoreCase) ? "⚠ " : "ℹ ")
                          .Append(n.Content);
            var cost = CostText;
            if (cost.Length > 0) sb.AppendLine().AppendLine().Append(cost);
            return sb.ToString();
        }
    }
}

public sealed class ModuleCategory
{
    [JsonPropertyName("display")] public string? Display { get; set; }
    [JsonPropertyName("modules")] public List<ModuleDef> Modules { get; set; } = new();

    [JsonIgnore] public string Key { get; set; } = "";
    public string Label => !string.IsNullOrWhiteSpace(Display) ? Display! : Naming.Prettify(Key);
    public override string ToString() => Label;
}

/// <summary>
/// The parsed mastercomfig module catalog. Category order is preserved from the source
/// document so the UI matches the order used by comfig.app and the docs.
/// </summary>
public sealed class ModuleCatalog
{
    public List<ModuleCategory> Categories { get; } = new();

    private readonly Dictionary<string, ModuleDef> _byName =
        new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, ModuleDef> ByName => _byName;

    public int ModuleCount => _byName.Count;

    public IEnumerable<ModuleDef> AllModules => Categories.SelectMany(c => c.Modules);

    public ModuleDef? Get(string name) =>
        _byName.TryGetValue(name, out var m) ? m : null;

    /// <summary>
    /// Parses the mastercomfig <c>data/modules.json</c> layout: a root object whose keys are
    /// category names and whose values hold an optional display label plus a "modules" array.
    /// Uses <see cref="JsonDocument"/> rather than a dictionary so key order is guaranteed.
    /// </summary>
    public static ModuleCatalog Parse(string json)
    {
        var catalog = new ModuleCatalog();
        var opts = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };

        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        });

        if (doc.RootElement.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Module catalog root is not a JSON object.");

        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            if (prop.Value.ValueKind != JsonValueKind.Object) continue;

            var cat = prop.Value.Deserialize<ModuleCategory>(opts) ?? new ModuleCategory();
            cat.Key = prop.Name;

            // Drop anything malformed rather than failing the whole load.
            cat.Modules.RemoveAll(m => string.IsNullOrWhiteSpace(m.Name) || m.Values.Count == 0);

            foreach (var m in cat.Modules)
            {
                m.CategoryKey = cat.Key;
                catalog._byName[m.Name] = m;
            }

            catalog.Categories.Add(cat);
        }

        if (catalog._byName.Count == 0)
            throw new InvalidDataException("Module catalog contained no usable modules.");

        return catalog;
    }
}

internal static class Naming
{
    /// <summary>"texture_quality" -> "Texture Quality"; "3dsky" -> "3Dsky".</summary>
    public static string Prettify(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return raw;
        var parts = raw.Replace('-', ' ').Replace('_', ' ')
                       .Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', parts.Select(p =>
            p.Length == 0 ? p : char.ToUpperInvariant(p[0]) + p[1..]));
    }
}
