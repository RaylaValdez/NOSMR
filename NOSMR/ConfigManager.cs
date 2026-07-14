using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;

namespace NOSMR;

/// <summary>
/// BepInEx configuration for NOSMR. Auto-generates a .cfg file at
/// BepInEx/config/com.gerryofravine.nosmr.cfg on first load.
/// </summary>
public class NOSMRConfig
{
    public ConfigEntry<bool> Enabled { get; }
    public ConfigEntry<string> RequiredMods { get; }
    public ConfigEntry<bool> EnableDebugLog { get; }

    /// <summary>Parsed set of required mod IDs (case-insensitive matching).</summary>
    public HashSet<string> RequiredModSet { get; private set; }

    public NOSMRConfig(ConfigFile config)
    {
        Enabled = config.Bind(
            "General",
            "Enabled",
            true,
            "Enable NOMM modlist broadcasting via A2S_RULES.\n" +
            "When disabled, NOMM keys are removed from the server query response."
        );

        RequiredMods = config.Bind(
            "General",
            "RequiredMods",
            "",
            "Comma-separated list of mod IDs that are required to join this server.\n" +
            "These are broadcast to NOMM clients for pre-join validation.\n" +
            "Leave empty to mark all mods as optional.\n" +
            "Example: aryx.f16m, no-autopilot-mod, SmokeTrail"
        );

        EnableDebugLog = config.Bind(
            "Debug",
            "EnableDebugLog",
            false,
            "Write detailed debug information to NOSMR/debug/debug.log.\n" +
            "Useful for troubleshooting, but increases log file size."
        );

        RequiredModSet = ParseRequiredMods(RequiredMods.Value);

        RequiredMods.SettingChanged += (_, _) =>
        {
            RequiredModSet = ParseRequiredMods(RequiredMods.Value);
        };
    }

    private static HashSet<string> ParseRequiredMods(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        return new HashSet<string>(
            raw.Split(',')
                .Select(s => s.Trim())
                .Where(s => s.Length > 0),
            StringComparer.OrdinalIgnoreCase
        );
    }
}
