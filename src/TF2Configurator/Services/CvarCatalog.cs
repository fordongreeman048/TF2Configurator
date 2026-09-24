namespace TF2Configurator.Services;

public enum CvarKind
{
    /// <summary>0 or 1, shown as a checkbox.</summary>
    Toggle,

    /// <summary>Free numeric value, shown as a text box.</summary>
    Number,

    /// <summary>Fixed set of values, shown as a combo box.</summary>
    Choice,

    /// <summary>Arbitrary string.</summary>
    Text,
}

public sealed class CvarDef
{
    public required string Name { get; init; }
    public required string Group { get; init; }
    public string? Display { get; init; }
    public string? Description { get; init; }
    public CvarKind Kind { get; init; } = CvarKind.Number;

    /// <summary>Allowed values for <see cref="CvarKind.Choice"/>, as (value, label) pairs.</summary>
    public (string Value, string Label)[]? Choices { get; init; }

    /// <summary>
    /// When set, a mastercomfig module normally controls this cvar. Setting it by hand in
    /// autoexec.cfg overrides the module — legal, but usually not intended.
    /// </summary>
    public string? ManagedByModule { get; init; }

    public string Label => Display ?? Models.Naming.Prettify(Name);

    public string Tooltip
    {
        get
        {
            var parts = new List<string> { Name };
            if (!string.IsNullOrWhiteSpace(Description)) parts.Add(Description!);
            if (ManagedByModule is not null)
                parts.Add($"⚠ Normally set by the mastercomfig '{ManagedByModule}' module. " +
                          "Setting it here overrides that module.");
            return string.Join("\r\n\r\n", parts);
        }
    }
}

/// <summary>
/// The console variables the autoexec and per-class editors offer. A shortlist of what people
/// actually tune, not every TF2 cvar. Anything not listed is still preserved on save and can
/// be edited on the raw text tab.
/// </summary>
public static class CvarCatalog
{
    private static readonly (string Value, string Label)[] OnOff =
    {
        ("0", "Off"),
        ("1", "On"),
    };

    public static readonly CvarDef[] Autoexec =
    {
        // ---- Networking -------------------------------------------------------------------
        new()
        {
            Name = "rate", Group = "Networking", Display = "Bandwidth (rate)",
            Kind = CvarKind.Number, ManagedByModule = "bandwidth",
            Description = "Maximum bytes per second the client will accept. 393216 suits most " +
                          "modern connections.",
        },
        new()
        {
            Name = "cl_cmdrate", Group = "Networking", Display = "Command rate",
            Kind = CvarKind.Number, ManagedByModule = "packet_rate",
            Description = "Packets sent to the server per second. TF2 servers cap this at 66.",
        },
        new()
        {
            Name = "cl_updaterate", Group = "Networking", Display = "Update rate",
            Kind = CvarKind.Number, ManagedByModule = "packet_rate",
            Description = "Snapshots requested from the server per second. Capped at 66.",
        },
        new()
        {
            Name = "cl_interp", Group = "Networking", Display = "Interpolation",
            Kind = CvarKind.Number, ManagedByModule = "snapshot_buffer",
            Description = "Interpolation delay in seconds. 0 lets cl_interp_ratio decide.",
        },
        new()
        {
            Name = "cl_interp_ratio", Group = "Networking", Display = "Interpolation ratio",
            Kind = CvarKind.Number, ManagedByModule = "snapshot_buffer",
            Description = "Interpolation expressed as a multiple of the snapshot interval. " +
                          "1 is the usual competitive choice; 2 tolerates packet loss better.",
        },
        new()
        {
            Name = "cl_lagcompensation", Group = "Networking",
            Kind = CvarKind.Choice, Choices = OnOff,
            Description = "Leave on. Disabling it breaks hit registration.",
        },
        new()
        {
            Name = "cl_pred_optimize", Group = "Networking", Display = "Prediction optimisation",
            Kind = CvarKind.Number,
            Description = "2 is the standard value and reduces prediction errors.",
        },
        new()
        {
            Name = "cl_smooth", Group = "Networking", Display = "Smooth prediction errors",
            Kind = CvarKind.Choice, Choices = OnOff,
            Description = "Off shows the true position instead of smoothing corrections.",
        },

        // ---- Frame rate -------------------------------------------------------------------
        new()
        {
            Name = "fps_max", Group = "Frame rate", Display = "FPS cap",
            Kind = CvarKind.Number, ManagedByModule = "fpscap",
            Description = "0 removes the cap. A cap slightly above your refresh rate gives the " +
                          "steadiest frame pacing.",
        },
        new()
        {
            Name = "cl_showfps", Group = "Frame rate", Display = "Show FPS counter",
            Kind = CvarKind.Choice,
            Choices = new[] { ("0", "Off"), ("1", "FPS"), ("2", "FPS + map"), ("4", "Smoothed") },
        },

        // ---- Viewmodels -------------------------------------------------------------------
        new()
        {
            Name = "viewmodel_fov", Group = "Viewmodels", Display = "Viewmodel FOV",
            Kind = CvarKind.Number,
            Description = "Field of view for your own weapon. 54 is default; 70-90 is common. " +
                          "Values above 90 need a mastercomfig addon or plugin to take effect.",
        },
        new()
        {
            Name = "r_drawviewmodel", Group = "Viewmodels", Display = "Show viewmodels",
            Kind = CvarKind.Choice, Choices = OnOff,
            Description = "Off hides weapons entirely, which frees up screen space and some GPU time.",
        },
        new()
        {
            Name = "tf_use_min_viewmodels", Group = "Viewmodels", Display = "Minimised viewmodels",
            Kind = CvarKind.Choice, Choices = OnOff,
        },
        new()
        {
            Name = "cl_first_person_uses_world_model", Group = "Viewmodels",
            Display = "First person world model",
            Kind = CvarKind.Choice, Choices = OnOff,
            Description = "Shows the full body in first person. Costs performance.",
        },

        // ---- Mouse ------------------------------------------------------------------------
        new()
        {
            Name = "sensitivity", Group = "Mouse", Kind = CvarKind.Number,
            Description = "Mouse sensitivity multiplier.",
        },
        new()
        {
            Name = "m_rawinput", Group = "Mouse", Display = "Raw input",
            Kind = CvarKind.Choice, Choices = OnOff,
            Description = "On bypasses Windows pointer acceleration and scaling. Recommended.",
        },
        new()
        {
            Name = "zoom_sensitivity_ratio", Group = "Mouse", Display = "Zoom sensitivity ratio",
            Kind = CvarKind.Number,
            Description = "Sensitivity multiplier while scoped as Sniper.",
        },
        new()
        {
            Name = "m_yaw", Group = "Mouse", Display = "Horizontal speed",
            Kind = CvarKind.Number, Description = "Default 0.022.",
        },
        new()
        {
            Name = "m_pitch", Group = "Mouse", Display = "Vertical speed",
            Kind = CvarKind.Number, Description = "Default 0.022.",
        },

        // ---- Crosshair --------------------------------------------------------------------
        new()
        {
            Name = "crosshair", Group = "Crosshair", Display = "Show crosshair",
            Kind = CvarKind.Choice, Choices = OnOff,
        },
        new()
        {
            Name = "cl_crosshair_file", Group = "Crosshair", Display = "Crosshair style",
            Kind = CvarKind.Choice,
            Choices = new[]
            {
                ("", "Default (per-weapon)"),
                ("crosshair1", "Crosshair 1 — small cross"),
                ("crosshair2", "Crosshair 2 — open cross"),
                ("crosshair3", "Crosshair 3 — circle"),
                ("crosshair4", "Crosshair 4 — dot in circle"),
                ("crosshair5", "Crosshair 5 — dot"),
                ("crosshair6", "Crosshair 6 — brackets"),
                ("crosshair7", "Crosshair 7 — thin cross"),
                ("default", "Weapon default"),
            },
        },
        new()
        {
            Name = "cl_crosshair_scale", Group = "Crosshair", Display = "Crosshair size",
            Kind = CvarKind.Number, Description = "Typical range 16-40.",
        },
        new() { Name = "cl_crosshair_red", Group = "Crosshair", Display = "Red (0-255)", Kind = CvarKind.Number },
        new() { Name = "cl_crosshair_green", Group = "Crosshair", Display = "Green (0-255)", Kind = CvarKind.Number },
        new() { Name = "cl_crosshair_blue", Group = "Crosshair", Display = "Blue (0-255)", Kind = CvarKind.Number },
        new()
        {
            Name = "cl_crosshairalpha", Group = "Crosshair", Display = "Opacity (0-255)",
            Kind = CvarKind.Number,
        },

        // ---- HUD and feedback -------------------------------------------------------------
        new()
        {
            Name = "hud_combattext", Group = "HUD", Display = "Damage numbers",
            Kind = CvarKind.Choice, Choices = OnOff,
        },
        new()
        {
            Name = "hud_combattext_healing", Group = "HUD", Display = "Healing numbers",
            Kind = CvarKind.Choice, Choices = OnOff,
        },
        new()
        {
            Name = "hud_combattext_batching", Group = "HUD", Display = "Combine damage numbers",
            Kind = CvarKind.Choice, Choices = OnOff,
            Description = "Adds up rapid hits into a single number instead of stacking them.",
        },
        new()
        {
            Name = "cl_hud_minmode", Group = "HUD", Display = "HUD minmode",
            Kind = CvarKind.Choice, Choices = OnOff,
            Description = "Compact HUD layout. Some custom HUDs change or ignore this.",
        },
        new()
        {
            Name = "tf_dingalingaling", Group = "HUD", Display = "Hit sound",
            Kind = CvarKind.Choice, Choices = OnOff,
        },
        new()
        {
            Name = "tf_dingaling_volume", Group = "HUD", Display = "Hit sound volume",
            Kind = CvarKind.Number, Description = "0.0 to 1.0.",
        },
        new()
        {
            Name = "tf_dingalingaling_lasthit", Group = "HUD", Display = "Kill sound",
            Kind = CvarKind.Choice, Choices = OnOff,
        },
        new()
        {
            Name = "voice_enable", Group = "HUD", Display = "Voice chat",
            Kind = CvarKind.Choice, Choices = OnOff, ManagedByModule = "voice_chat",
        },

        // ---- Gameplay quality of life -----------------------------------------------------
        new()
        {
            Name = "cl_autoreload", Group = "Gameplay", Display = "Auto reload",
            Kind = CvarKind.Choice, Choices = OnOff,
        },
        new()
        {
            Name = "cl_disablefreezecam", Group = "Gameplay", Display = "Disable freezecam",
            Kind = CvarKind.Choice, Choices = OnOff,
        },
        new()
        {
            Name = "tf_remember_activeweapon", Group = "Gameplay",
            Display = "Remember active weapon",
            Kind = CvarKind.Choice, Choices = OnOff,
        },
        new()
        {
            Name = "tf_remember_lastswitched", Group = "Gameplay",
            Display = "Remember last switched weapon",
            Kind = CvarKind.Choice, Choices = OnOff,
        },
        new()
        {
            Name = "tf_scoreboard_ping_as_text", Group = "Gameplay",
            Display = "Numeric ping on scoreboard",
            Kind = CvarKind.Choice, Choices = OnOff,
        },
        new()
        {
            Name = "cl_ask_blacklist_opt_out", Group = "Gameplay",
            Display = "Hide server blacklist prompt",
            Kind = CvarKind.Choice, Choices = OnOff,
        },
        new()
        {
            Name = "cl_ask_favorite_opt_out", Group = "Gameplay",
            Display = "Hide favourite server prompt",
            Kind = CvarKind.Choice, Choices = OnOff,
        },
    };

    /// <summary>
    /// Settings worth varying per class. Interp in particular is commonly tuned per class
    /// (projectile classes tolerate more than hitscan ones).
    /// </summary>
    public static readonly CvarDef[] ClassConfig =
    {
        new()
        {
            Name = "viewmodel_fov", Group = "Viewmodels", Display = "Viewmodel FOV",
            Kind = CvarKind.Number,
        },
        new()
        {
            Name = "r_drawviewmodel", Group = "Viewmodels", Display = "Show viewmodels",
            Kind = CvarKind.Choice, Choices = OnOff,
        },
        new()
        {
            Name = "tf_use_min_viewmodels", Group = "Viewmodels", Display = "Minimised viewmodels",
            Kind = CvarKind.Choice, Choices = OnOff,
        },
        new()
        {
            Name = "cl_crosshair_file", Group = "Crosshair", Display = "Crosshair style",
            Kind = CvarKind.Choice,
            Choices = new[]
            {
                ("", "Default (per-weapon)"),
                ("crosshair1", "Crosshair 1 — small cross"),
                ("crosshair2", "Crosshair 2 — open cross"),
                ("crosshair3", "Crosshair 3 — circle"),
                ("crosshair4", "Crosshair 4 — dot in circle"),
                ("crosshair5", "Crosshair 5 — dot"),
                ("crosshair6", "Crosshair 6 — brackets"),
                ("crosshair7", "Crosshair 7 — thin cross"),
                ("default", "Weapon default"),
            },
        },
        new()
        {
            Name = "cl_crosshair_scale", Group = "Crosshair", Display = "Crosshair size",
            Kind = CvarKind.Number,
        },
        new() { Name = "cl_crosshair_red", Group = "Crosshair", Display = "Red (0-255)", Kind = CvarKind.Number },
        new() { Name = "cl_crosshair_green", Group = "Crosshair", Display = "Green (0-255)", Kind = CvarKind.Number },
        new() { Name = "cl_crosshair_blue", Group = "Crosshair", Display = "Blue (0-255)", Kind = CvarKind.Number },
        new()
        {
            Name = "sensitivity", Group = "Mouse", Kind = CvarKind.Number,
            Description = "Per-class sensitivity. Remember that this persists after switching " +
                          "class unless every class config sets it.",
        },
        new()
        {
            Name = "zoom_sensitivity_ratio", Group = "Mouse", Display = "Zoom sensitivity ratio",
            Kind = CvarKind.Number,
        },
        new()
        {
            Name = "cl_interp", Group = "Networking", Display = "Interpolation",
            Kind = CvarKind.Number, ManagedByModule = "snapshot_buffer",
        },
        new()
        {
            Name = "cl_interp_ratio", Group = "Networking", Display = "Interpolation ratio",
            Kind = CvarKind.Number, ManagedByModule = "snapshot_buffer",
        },
        new()
        {
            Name = "cl_autoreload", Group = "Gameplay", Display = "Auto reload",
            Kind = CvarKind.Choice, Choices = OnOff,
        },
    };
}
