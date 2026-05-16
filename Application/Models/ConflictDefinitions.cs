using System;
using System.Collections.Generic;
using System.Linq;

namespace ToolKitV.Models
{
    public class ConflictCategory
    {
        public string Title       { get; }
        public string Description { get; }
        public HashSet<string> MutuallyExclusiveScripts { get; }

        public ConflictCategory(string title, string description, params string[] scripts)
        {
            Title       = title;
            Description = description;
            MutuallyExclusiveScripts = new HashSet<string>(scripts, StringComparer.OrdinalIgnoreCase);
        }
    }

    public static class ConflictDefinitions
    {
        // ─── Framework defaults that should always lose to a premium replacement ──
        public static readonly HashSet<string> FrameworkDefaultScripts =
            new(StringComparer.OrdinalIgnoreCase)
        {
            // QBCore defaults
            "qb-inventory", "qb-phone", "qb-garages", "qb-ambulancejob",
            "qb-policejob",  "qb-mechanicjob", "qb-clothing", "qb-hud",
            "qb-target", "qb-weathersync", "qb-banking", "qb-vehiclekeys",
            "qb-doorlock", "qb-notify",
            // Qbox defaults
            "qbx_inventory", "qbx_garages", "qbx_medical", "qbx_police",
            "qbx_clothing",  "qbx_target",
        };

        // ─── Conflict categories ──────────────────────────────────────────────────
        public static readonly List<ConflictCategory> Categories = new()
        {
            new ConflictCategory(
                "MDT System",
                "Multiple Police/EMS databases running simultaneously will corrupt SQL data.",
                "xd_mdt", "jpr-mdtsystem", "redutzu-mdt", "ps-mdt", "mdt"),

            new ConflictCategory(
                "Phone System",
                "Running multiple phones causes NUI overlaps and webhook rate limiting. Disable the framework default.",
                "jpr-phonesystem", "lb-phone", "qs-smartphone", "high_phone", "renewed-phone", "gksphone",
                "qb-phone" /* QBCore default */),

            new ConflictCategory(
                "Inventory System",
                "Overlapping inventories duplicate items and break the weapon wheel. Disable the framework default.",
                "ox_inventory", "qs-inventory", "jpr-inventory", "ps-inventory", "core_inventory",
                "qb-inventory" /* QBCore default */, "qbx_inventory" /* Qbox default */),

            new ConflictCategory(
                "Ambulance & Medical",
                "Multiple death systems cause players to instantly revive or get stuck in the death animation.",
                "wasabi_ambulance", "xd_ambulancejob",
                "qb-ambulancejob" /* QBCore default */, "qbx_medical" /* Qbox default */, "esx_ambulancejob"),

            new ConflictCategory(
                "Police Job",
                "Multiple police jobs cross-wire duty statuses, dispatch alerts, and armories.",
                "wasabi_police", "jpr-policejob",
                "qb-policejob" /* QBCore default */, "qbx_police" /* Qbox default */),

            new ConflictCategory(
                "Targeting (Third Eye)",
                "Multiple raycast scripts simultaneously cause massive client FPS drops.",
                "ox_target",
                "qb-target" /* QBCore default */, "qbx_target" /* Qbox default */, "qtarget", "bt-target"),

            new ConflictCategory(
                "Chop Shop",
                "Overlapping chop shop polyzones cause vehicles to despawn or reward incorrectly.",
                "xd_chopshop", "lation_chopshop", "qb-chopshop"),

            new ConflictCategory(
                "Mechanic & Tuning",
                "Multiple tuning scripts cause part duplication, menu overlaps, and prop conflicts.",
                "jpr-mechanic", "jg-mechanic", "onx-tuning", "xd_freetuner",
                "qb-mechanicjob" /* QBCore default */),

            new ConflictCategory(
                "Garage System",
                "Multiple garage systems corrupt vehicle ownership SQL tables and duplicate cars.",
                "jpr-garages", "jg-advancedgarages", "cd_garage",
                "qb-garages" /* QBCore default */, "qbx_garages" /* Qbox default */),

            new ConflictCategory(
                "Clothing & Appearance",
                "Running two clothing scripts causes double-rendering — peds wearing two outfits simultaneously.",
                "illenium-appearance", "fivem-appearance", "dpclothing",
                "qb-clothing" /* QBCore default */, "qbx_clothing" /* Qbox default */),

            new ConflictCategory(
                "HUD & UI",
                "Multiple HUDs stack on top of each other and progressively drain client FPS.",
                "xd_hud", "codem-hud", "ps-hud",
                "qb-hud" /* QBCore default */),

            new ConflictCategory(
                "Voice Chat",
                "Running two voice backends simultaneously breaks all proximity audio.",
                "pma-voice", "saltychat", "mumble-voip"),

            new ConflictCategory(
                "Fuel System",
                "Multiple fuel scripts write conflicting metadata values to the same vehicle property.",
                "ox_fuel", "ps-fuel", "cd_fuel", "LegacyFuel"),

            new ConflictCategory(
                "Notification Library",
                "Multiple notification libraries firing on the same events produces doubled pop-ups.",
                "lation_ui", "okokNotify", "mythic_notify",
                "qb-notify" /* QBCore default */),
        };

        // ─── Ecosystem-aware winner selection ─────────────────────────────────────

        /// <summary>
        /// Returns the ecosystem-preferred winner for a conflict category.
        /// Framework defaults always lose to any premium replacement.
        /// Priority order mirrors the Auto-Wirer's ecosystem detection.
        /// </summary>
        public static string? GetPreferredWinner(ConflictCategory cat, HashSet<string> installed)
        {
            // Premium-first preference chains per category
            var preferences = cat.Title switch
            {
                "MDT System"            => new[] { "jpr-mdtsystem", "xd_mdt", "redutzu-mdt", "ps-mdt" },
                "Phone System"          => new[] { "jpr-phonesystem", "lb-phone", "qs-smartphone", "renewed-phone", "high_phone", "gksphone" },
                "Inventory System"      => new[] { "ox_inventory", "qs-inventory", "jpr-inventory", "ps-inventory", "core_inventory" },
                "Ambulance & Medical"   => new[] { "wasabi_ambulance", "xd_ambulancejob" },
                "Police Job"            => new[] { "wasabi_police", "jpr-policejob" },
                "Targeting (Third Eye)" => new[] { "ox_target", "qtarget", "bt-target" },
                "Chop Shop"             => new[] { "lation_chopshop", "xd_chopshop" },
                "Mechanic & Tuning"     => new[] { "jpr-mechanic", "jg-mechanic", "onx-tuning", "xd_freetuner" },
                "Garage System"         => new[] { "jpr-garages", "jg-advancedgarages", "cd_garage" },
                "Clothing & Appearance" => new[] { "illenium-appearance", "fivem-appearance", "dpclothing" },
                "HUD & UI"              => new[] { "xd_hud", "codem-hud", "ps-hud" },
                "Voice Chat"            => new[] { "pma-voice", "saltychat" },
                "Fuel System"           => new[] { "ox_fuel", "ps-fuel", "cd_fuel" },
                "Notification Library"  => new[] { "lation_ui", "okokNotify", "mythic_notify" },
                _                       => Array.Empty<string>()
            };

            // 1. Try premium-first preference chain
            foreach (var script in preferences)
                if (installed.Contains(script)) return script;

            // 2. Fallback: any installed non-default script
            var premiumFallback = cat.MutuallyExclusiveScripts
                .FirstOrDefault(s => installed.Contains(s) && !FrameworkDefaultScripts.Contains(s));
            if (premiumFallback != null) return premiumFallback;

            // 3. Last resort: first installed script (even if it's a default)
            return cat.MutuallyExclusiveScripts.FirstOrDefault(installed.Contains);
        }
    }
}
