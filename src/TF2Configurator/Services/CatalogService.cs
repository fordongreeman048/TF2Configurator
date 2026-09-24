using System.Reflection;
using System.Text.Json;
using TF2Configurator.Models;

namespace TF2Configurator.Services;

/// <summary>
/// Holds the module catalog and preset data. Snapshots of mastercomfig's JSON files (modules,
/// presets, per-level cvars) are embedded so the app works offline; newer copies can be
/// downloaded and are cached per-user. A download is only cached after it parses cleanly, so
/// a bad response can't break startup.
/// </summary>
public sealed class CatalogService
{
    private const string RawBase =
        "https://raw.githubusercontent.com/mastercomfig/mastercomfig/develop/data/";

    private const string ResourceBase = "TF2Configurator.";

    private static readonly HttpClient Http = CreateHttp();

    private static HttpClient CreateHttp()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("TF2Configurator/1.0");
        return http;
    }

    public static string CacheDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                     "TF2Configurator");

    public static string CachePath => Path.Combine(CacheDir, "modules.json");
    public static string PresetCachePath => Path.Combine(CacheDir, "preset_modules.json");
    public static string ValuesCachePath => Path.Combine(CacheDir, "module_values.json");

    /// <summary>Where the currently loaded catalog came from, for display in the UI.</summary>
    public string Source { get; private set; } = "bundled";

    public DateTime? CacheTimestamp { get; private set; }

    public ModuleCatalog Load()
    {
        // Prefer a validated cached download; fall back to the embedded snapshot.
        try
        {
            if (File.Exists(CachePath))
            {
                var catalog = ModuleCatalog.Parse(File.ReadAllText(CachePath));
                CacheTimestamp = File.GetLastWriteTime(CachePath);
                Source = $"downloaded {CacheTimestamp:yyyy-MM-dd HH:mm}";
                return catalog;
            }
        }
        catch
        {
            // Corrupt cache: ignore it and use the bundled copy.
        }

        Source = "bundled snapshot";
        CacheTimestamp = null;
        return ModuleCatalog.Parse(ReadEmbedded("modules.json"));
    }

    /// <summary>
    /// Loads the preset + per-level cvar data, preferring a validated cached download. Each file
    /// falls back to its own embedded copy independently — a preset mapping without its cvar
    /// details is still usable.
    /// </summary>
    public PresetCatalog LoadPresets()
    {
        var preset = TryReadCached(PresetCachePath) ?? ReadEmbedded("preset_modules.json");
        var values = TryReadCached(ValuesCachePath) ?? ReadEmbedded("module_values.json");
        return PresetCatalog.Parse(preset, values);
    }

    private static string? TryReadCached(string path)
    {
        try
        {
            if (File.Exists(path)) return File.ReadAllText(path);
        }
        catch
        {
            // Unreadable cache: fall back to the embedded copy.
        }

        return null;
    }

    public static string ReadEmbedded(string fileName)
    {
        var asm = Assembly.GetExecutingAssembly();
        var name = ResourceBase + fileName;
        using var stream = asm.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException(
                $"Embedded resource '{name}' is missing from the build.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// Downloads the latest catalog and preset data and caches them. Returns the parsed catalog
    /// on success; throws with a readable message on failure (the caller keeps the existing
    /// data). Each file is parsed before caching, so a bad payload never becomes the stored copy.
    /// </summary>
    public async Task<ModuleCatalog> RefreshAsync(CancellationToken ct = default)
    {
        // Fetch all three in parallel; a failure in any of them aborts the whole refresh so the
        // cache never holds a half-updated set.
        var modulesTask = Http.GetStringAsync(RawBase + "modules.json", ct);
        var presetsTask = Http.GetStringAsync(RawBase + "preset_modules.json", ct);
        var valuesTask = Http.GetStringAsync(RawBase + "module_values.json", ct);

        await Task.WhenAll(modulesTask, presetsTask, valuesTask).ConfigureAwait(false);

        var modulesJson = await modulesTask.ConfigureAwait(false);
        var presetsJson = await presetsTask.ConfigureAwait(false);
        var valuesJson = await valuesTask.ConfigureAwait(false);

        // Parse everything before caching so a bad payload never becomes the stored data.
        var catalog = ModuleCatalog.Parse(modulesJson);
        PresetCatalog.Parse(presetsJson, valuesJson);

        Directory.CreateDirectory(CacheDir);
        await File.WriteAllTextAsync(CachePath, modulesJson, ct).ConfigureAwait(false);
        await File.WriteAllTextAsync(PresetCachePath, presetsJson, ct).ConfigureAwait(false);
        await File.WriteAllTextAsync(ValuesCachePath, valuesJson, ct).ConfigureAwait(false);

        CacheTimestamp = DateTime.Now;
        Source = $"downloaded {CacheTimestamp:yyyy-MM-dd HH:mm}";
        return catalog;
    }

    /// <summary>Deletes the cached downloads so the bundled snapshots are used again.</summary>
    public void ClearCache()
    {
        foreach (var path in new[] { CachePath, PresetCachePath, ValuesCachePath })
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch
            {
                // Non-fatal.
            }
        }

        Source = "bundled snapshot";
        CacheTimestamp = null;
    }
}

/// <summary>Small JSON-backed settings store in %AppData%\TF2Configurator\settings.json.</summary>
public sealed class AppSettings
{
    public string? Tf2PathOverride { get; set; }
    public string? LaunchOptions { get; set; }

    /// <summary>Last time the catalog was checked for updates, to pace the passive check.</summary>
    public DateTime? LastCatalogCheck { get; set; }

    private static string FilePath => Path.Combine(CatalogService.CacheDir, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath))
                       ?? new AppSettings();
        }
        catch
        {
            // Unreadable settings fall back to defaults.
        }

        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(CatalogService.CacheDir);
            File.WriteAllText(FilePath,
                JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // Settings are a convenience; failing to persist them must not break the app.
        }
    }
}
