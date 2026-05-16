using System;
using System.Collections.Generic;

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
        public static readonly List<ConflictCategory> Categories = new()
        {
            new ConflictCategory(
                "MDT System",
                "Multiple Police/EMS databases running simultaneously will corrupt SQL data.",
                "xd_mdt", "jpr-mdtsystem", "redutzu-mdt", "ps-mdt", "mdt"),

            new ConflictCategory(
                "Phone System",
                "Running multiple phones causes NUI overlay conflicts and webhook rate limiting.",
                "jpr-phonesystem", "lb-phone", "qs-smartphone", "qb-phone", "high_phone", "renewed-phone", "gksphone"),

            new ConflictCategory(
                "Inventory System",
                "Overlapping inventories will duplicate items and break the weapon wheel.",
                "ox_inventory", "qs-inventory", "jpr-inventory", "ps-inventory", "qb-inventory", "core_inventory", "linden_inventory"),

            new ConflictCategory(
                "Ambulance / Death System",
                "Multiple death systems cause players to instantly revive or get stuck in geometry.",
                "xd_ambulancejob", "wasabi_ambulance", "qb-ambulancejob", "esx_ambulancejob"),

            new ConflictCategory(
                "Targeting (Third Eye)",
                "Multiple raycast scripts simultaneously cause massive client FPS drops.",
                "ox_target", "qb-target", "qtarget", "bt-target"),

            new ConflictCategory(
                "Chop Shop",
                "Overlapping chop shop polyzones cause vehicles to despawn or reward incorrectly.",
                "xd_chopshop", "lation_chopshop", "qb-chopshop"),

            new ConflictCategory(
                "Mechanic / Tuning",
                "Multiple mechanic scripts conflict on prop streaming and export names.",
                "jpr-mechanic", "jg-mechanic", "onx-tuning", "xd_freetuner"),

            new ConflictCategory(
                "Garage System",
                "Multiple garage systems conflict on vehicle ownership SQL tables.",
                "jpr-garages", "jg-advancedgarages", "cd_garage", "qs-housing", "qb-garages"),

            new ConflictCategory(
                "Voice Chat",
                "Running two voice backends simultaneously breaks all proximity audio.",
                "pma-voice", "saltychat", "mumble-voip"),

            new ConflictCategory(
                "Fuel System",
                "Multiple fuel scripts write conflicting metadata values to the same vehicle property.",
                "ox_fuel", "ps-fuel", "LegacyFuel", "cd_fuel"),

            new ConflictCategory(
                "Notification / UI",
                "Multiple notification libraries firing on the same events causes doubled pop-ups.",
                "lation_ui", "okokNotify", "mythic_notify", "qb-notify"),
        };

        /// <summary>
        /// Returns ecosystem-preferred winner for a category given the installed resource set.
        /// Priority mirrors the Auto-Wirer's ecosystem detection order.
        /// </summary>
        public static string? GetPreferredWinner(ConflictCategory cat, HashSet<string> installed)
        {
            // Ordered preference lists per category (most modern/preferred first)
            var preferences = cat.Title switch
            {
                "MDT System"             => new[] { "jpr-mdtsystem", "xd_mdt", "redutzu-mdt", "ps-mdt" },
                "Phone System"           => new[] { "jpr-phonesystem", "lb-phone", "qs-smartphone", "renewed-phone", "high_phone", "gksphone", "qb-phone" },
                "Inventory System"       => new[] { "ox_inventory", "qs-inventory", "jpr-inventory", "ps-inventory", "core_inventory", "qb-inventory" },
                "Ambulance / Death System" => new[] { "wasabi_ambulance", "xd_ambulancejob", "qb-ambulancejob", "esx_ambulancejob" },
                "Targeting (Third Eye)"  => new[] { "ox_target", "qb-target", "qtarget", "bt-target" },
                "Chop Shop"              => new[] { "lation_chopshop", "xd_chopshop", "qb-chopshop" },
                "Mechanic / Tuning"      => new[] { "jpr-mechanic", "jg-mechanic", "onx-tuning", "xd_freetuner" },
                "Garage System"          => new[] { "jpr-garages", "jg-advancedgarages", "cd_garage", "qs-housing", "qb-garages" },
                "Voice Chat"             => new[] { "pma-voice", "saltychat", "mumble-voip" },
                "Fuel System"            => new[] { "ox_fuel", "ps-fuel", "cd_fuel", "LegacyFuel" },
                "Notification / UI"      => new[] { "lation_ui", "okokNotify", "mythic_notify", "qb-notify" },
                _                        => Array.Empty<string>()
            };

            foreach (var script in preferences)
                if (installed.Contains(script)) return script;

            return null;
        }
    }
}
