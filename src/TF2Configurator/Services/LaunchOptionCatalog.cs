namespace TF2Configurator.Services;

public enum LaunchOptionKind
{
    /// <summary>A bare switch, e.g. <c>-novid</c>.</summary>
    Flag,

    /// <summary>A switch with an argument, e.g. <c>-particles 1</c>.</summary>
    Value,
}

public sealed class LaunchOptionDef
{
    public required string Switch { get; init; }
    public required string Group { get; init; }
    public string? Display { get; init; }
    public string? Description { get; init; }
    public LaunchOptionKind Kind { get; init; } = LaunchOptionKind.Flag;

    /// <summary>Prefilled argument for <see cref="LaunchOptionKind.Value"/> options.</summary>
    public string? DefaultValue { get; init; }

    /// <summary>Part of mastercomfig's recommended set; ticked by "Apply recommended".</summary>
    public bool Recommended { get; init; }

    /// <summary>Shown as a warning in the UI. Non-null means "understand this before using it".</summary>
    public string? Caution { get; init; }

    public string Label => Display ?? Switch;
}

/// <summary>
/// The launch options the builder offers, grouped for presentation. The recommended set matches
/// mastercomfig's docs. Options that can hurt performance or reset a config carry an explicit
/// caution instead of being hidden — those are the ones people copy from bad guides.
/// </summary>
public static class LaunchOptionCatalog
{
    public static readonly LaunchOptionDef[] All =
    {
        // ---- Recommended ------------------------------------------------------------------
        new()
        {
            Switch = "-novid", Group = "Recommended", Recommended = true,
            Display = "Skip intro video",
            Description = "Skips the Valve intro movie on startup.",
        },
        new()
        {
            Switch = "-nojoy", Group = "Recommended", Recommended = true,
            Display = "Disable joystick support",
            Description = "Skips loading joystick code, which shortens startup and saves a little memory.",
        },
        new()
        {
            Switch = "-nosteamcontroller", Group = "Recommended", Recommended = true,
            Display = "Disable Steam Controller",
            Description = "Skips Steam Controller initialisation.",
        },
        new()
        {
            Switch = "-nohltv", Group = "Recommended", Recommended = true,
            Display = "Disable SourceTV",
            Description = "Disables SourceTV support. Safe unless you host demos via SourceTV.",
        },
        new()
        {
            Switch = "-particles", Group = "Recommended", Recommended = true,
            Kind = LaunchOptionKind.Value, DefaultValue = "1",
            Display = "Particle buffer",
            Description = "Lowers the particle memory pool. 1 is mastercomfig's recommendation.",
        },
        new()
        {
            Switch = "-precachefontchars", Group = "Recommended", Recommended = true,
            Display = "Precache font characters",
            Description = "Precaches glyphs to avoid stutter the first time text is drawn.",
        },
        new()
        {
            Switch = "-noquicktime", Group = "Recommended", Recommended = true,
            Display = "Disable QuickTime",
            Description = "Skips loading the unused QuickTime video backend.",
        },

        // ---- Display ----------------------------------------------------------------------
        new()
        {
            Switch = "-fullscreen", Group = "Display", Display = "Fullscreen",
            Description = "Force exclusive fullscreen. Do not combine with -windowed.",
        },
        new()
        {
            Switch = "-windowed", Group = "Display", Display = "Windowed",
            Description = "Run in a window. Combine with -noborder for borderless.",
        },
        new()
        {
            Switch = "-noborder", Group = "Display", Display = "Borderless",
            Description = "Removes the window border. Requires -windowed.",
        },
        new()
        {
            Switch = "-w", Group = "Display", Kind = LaunchOptionKind.Value,
            Display = "Width", Description = "Horizontal resolution in pixels.",
        },
        new()
        {
            Switch = "-h", Group = "Display", Kind = LaunchOptionKind.Value,
            Display = "Height", Description = "Vertical resolution in pixels.",
        },
        new()
        {
            Switch = "-freq", Group = "Display", Kind = LaunchOptionKind.Value,
            Display = "Refresh rate", Description = "Refresh rate in Hz for fullscreen.",
        },

        // ---- Advanced ---------------------------------------------------------------------
        new()
        {
            Switch = "-dxlevel", Group = "Advanced", Kind = LaunchOptionKind.Value,
            DefaultValue = "95", Display = "DirectX level",
            Description = "Forces a DirectX feature level. 95 is the usual choice.",
            Caution = "Resets video settings every launch while present. Run the game once, " +
                      "then remove it.",
        },
        new()
        {
            Switch = "-high", Group = "Advanced", Display = "High process priority",
            Description = "Starts TF2 at high CPU priority.",
            Caution = "Can starve background processes and cause stutter or audio crackle. " +
                      "Try it, do not assume it helps.",
        },
        new()
        {
            Switch = "-threads", Group = "Advanced", Kind = LaunchOptionKind.Value,
            Display = "Worker threads",
            Description = "Overrides the size of the engine thread pool.",
            Caution = "Usually reduces performance and can cause crashes. Leave it off unless " +
                      "you have measured an improvement.",
        },
        new()
        {
            Switch = "-autoconfig", Group = "Advanced", Display = "Reset to auto config",
            Description = "Resets video and performance settings to detected defaults.",
            Caution = "Overwrites your video settings on every launch while present. " +
                      "Use once, then remove.",
        },
        new()
        {
            Switch = "-condebug", Group = "Advanced", Display = "Log console to file",
            Description = "Writes all console output to tf/console.log. Useful for diagnostics.",
        },
        new()
        {
            Switch = "-nostartupsound", Group = "Advanced", Display = "Skip startup sound",
        },
        new()
        {
            Switch = "-sw", Group = "Advanced", Display = "Software windowed mode",
            Caution = "Legacy switch; rarely useful on modern systems.",
        },
    };

    public static IEnumerable<string> Groups =>
        All.Select(o => o.Group).Distinct();
}
