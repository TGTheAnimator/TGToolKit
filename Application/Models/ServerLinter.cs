using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ToolKitV.Models.Providers;

namespace ToolKitV.Models
{
    public static class ServerLinter
    {
        // ─── Warning severity ────────────────────────────────────────────────────

        public enum Severity { Info = 0, Warning = 1, Critical = 2 }

        public class LinterWarning
        {
            public string   ResourceName { get; init; } = string.Empty;
            public string   Message      { get; init; } = string.Empty;
            public Severity Severity     { get; init; } = Severity.Warning;
        }

        public class LinterResult
        {
            public int                  ResourcesScanned        { get; set; }
            public int                  ResourcesWithIssues     { get; set; }
            public List<LinterWarning>  Warnings                { get; set; } = new();
            /// <summary>
            /// Full local file paths of any deprecated __resource.lua files found.
            /// Populated only in Local mode — SFTP mode has no writable paths.
            /// </summary>
            public List<string>         DeprecatedManifestPaths { get; set; } = new();
            /// <summary>
            /// All resource folder-names discovered during the scan.
            /// Used by the Auto-Wirer to build the server's dependency graph.
            /// </summary>
            public HashSet<string>      InstalledResources      { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        }

        // ─── Known integration hints ─────────────────────────────────────────────

        private static readonly Dictionary<string, string> KnownIntegrations =
            new(StringComparer.OrdinalIgnoreCase)
        {
            // ════════ CORE & DATABASES ════════
            ["oxmysql"]              = "oxmysql: Requires 'mysql_connection_string' convar in server.cfg. Essential for all modern DB scripts.",
            ["ox_lib"]               = "ox_lib: Core dependency. Scripts must declare 'shared_script \"@ox_lib/init.lua\"' in their fxmanifest.",
            ["mysql-async"]          = "[DEPRECATED] mysql-async: Obsolete. Migrate to oxmysql to prevent thread hitching and connection leaks.",
            ["ghmattimysql"]         = "[DEPRECATED] ghmattimysql: Obsolete. Migrate to oxmysql.",

            // ════════ INVENTORIES ════════
            ["ox_inventory"]         = "ox_inventory: Disable default framework inventories (qb-inventory/esx_inventory) and default weapon scripts.",
            ["qs-inventory"]         = "qs-inventory: Requires running the provided SQL file. Disable default weapon HUDs to prevent overlap.",
            ["ps-inventory"]         = "ps-inventory: Requires custom UI images mapped correctly in HTML. Ensure qb-inventory is stopped.",
            ["core_inventory"]       = "core_inventory: Ensure SQL schema is executed and metadata flags are properly set in config.",
            ["linden_inventory"]     = "[LEGACY] linden_inventory: Consider upgrading to ox_inventory for modern support.",

            // ════════ TARGETING (THIRD EYE) ════════
            ["ox_target"]            = "ox_target: Must start BEFORE scripts that rely on it. Disable qb-target to prevent third-eye conflicts.",
            ["qb-target"]            = "qb-target: Ensure 'interact-sound' is installed for UI click sounds.",
            ["qtarget"]              = "[DEPRECATED] qtarget: Superseded by ox_target. Migrate for improved performance.",

            // ════════ VOICE & AUDIO ════════
            ["pma-voice"]            = "pma-voice: Requires convars 'voice_useNativeAudio' and 'voice_use3dAudio'. Ensure mumble-voip is disabled.",
            ["saltychat"]            = "saltychat: Requires a dedicated TeamSpeak server integration. Not compatible with pma-voice.",
            ["xsound"]               = "xsound: Required by many boombox/DJ scripts. Ensure YouTube/Stream API rate limits aren't blocking audio.",
            ["interact-sound"]       = "interact-sound: Client-side .ogg files must be placed in interact-sound/client/html/sounds/.",

            // ════════ PHONES ════════
            ["lb-phone"]             = "lb-phone: Heavy resource. Requires oxmysql and its specific map assets streamed correctly. Disable default framework phones.",
            ["qs-smartphone"]        = "qs-smartphone: Demands specific SQL structure for contacts/messages. Disable default framework phones.",
            ["gksphone"]             = "gksphone: Requires webhooks and external API keys (Weather/Crypto) configured in config.json.",
            ["renewed-phone"]        = "renewed-phone: Requires ox_lib. Ensure all older phone scripts are disabled.",
            ["high_phone"]           = "high_phone: Requires running extensive SQL migrations for app data tables.",

            // ════════ GARAGES, VEHICLES & KEYS ════════
            ["jg-advancedgarages"]   = "jg-advancedgarages: Disable default garages. Ensure impound police job names match your framework config.",
            ["jg-dealerships"]       = "jg-dealerships: Vehicles must be defined in the jg-dealerships SQL table, NOT the default framework vehicle tables.",
            ["qs-vehiclekeys"]       = "qs-vehiclekeys: Disable qb-vehiclekeys or any default key scripts to prevent unlock desync.",
            ["wasabi_carlock"]       = "wasabi_carlock: Replaces default lock scripts. Check export compatibility with your garage system.",
            ["cd_garage"]            = "cd_garage: Execute SQL schema and ensure shell/interior dependencies start prior.",
            ["jp-keys"]              = "jp-keys: Replaces default vehicle key scripts (e.g., qb-vehiclekeys). Stop old key scripts first.",

            // ════════ CLOTHING & APPEARANCE ════════
            ["illenium-appearance"]  = "illenium-appearance: Requires complete DB clothing table migration. Run migration SQL and set cl_enableLargeFetch=1 in server.cfg.",
            ["fivem-appearance"]     = "fivem-appearance: Requires correct SQL table for outfit saving. Disable qb-clothing or esx_skin.",
            ["dpclothing"]           = "dpclothing: If using DB-saved keybinds, the provided SQL must be imported first.",

            // ════════ UI, HUD & NOTIFICATIONS ════════
            ["okokNotify"]           = "okokNotify: Replaces default notifications. Ensure your framework config points to okok exports.",
            ["okokChat"]             = "okokChat: Disable default 'chat' resource to prevent double-rendering.",
            ["okokTextUI"]           = "okokTextUI: Ensure scripts using DrawText point to the okok export rather than the framework default.",
            ["codem-hud"]            = "codem-hud: Disable default HUDs (qb-hud). Configure seatbelt/fuel exports in codem config.",
            ["ps-ui"]                = "ps-ui: Minigame/UI library. Scripts calling its exports MUST NOT load before ps-ui starts.",
            ["progressbar"]          = "progressbar: Ensure NUI callbacks are not blocked by another resource serving the same endpoint.",
            ["screenshot-basic"]     = "screenshot-basic: Requires Discord webhook URL and a compatible game build for canvas capture.",

            // ════════ POLICE, EMS & DISPATCH ════════
            ["ps-dispatch"]          = "ps-dispatch: Requires modifying core framework events (shooting/stealing) to trigger alerts. Follow the README injection guide.",
            ["qs-dispatch"]          = "qs-dispatch: Ensure job arrays are configured so EMS and Police don't receive crossed alert calls.",
            ["wasabi_ambulance"]     = "wasabi_ambulance: Replaces default death systems. MUST completely disable qb-ambulancejob/esx_ambulancejob.",
            ["wasabi_police"]        = "wasabi_police: Requires custom items (handcuffs, keys) in your inventory. Ensure evidence systems don't overlap with core framework.",
            ["wasabi_evidence"]      = "wasabi_evidence: Ensure bullet casing models and blood drops don't conflict with any active cleanup scripts.",
            ["wasabi_multijob"]      = "wasabi_multijob: Requires database schema execution on first install. Verify framework default job-setting logic isn't interfering.",

            // ════════ LATION SCRIPTS ════════
            ["lation_core"]          = "lation_core: Required for ALL Lation scripts. Must load BEFORE any other lation_* resource in server.cfg.",
            ["lation_chopshop"]      = "lation_chopshop: Requires a targeting script (ox_target/qb-target). Ensure coords don't conflict with MLOs.",
            ["lation_weed"]          = "lation_weed: Requires proper item registration (ox/qb). Check config.lua for PolyZone setups.",
            ["lation_laundering"]    = "lation_laundering: Relies on framework dirty money items (e.g., 'markedbills'). Verify item names in your inventory config.",

            // ════════ RCORE SCRIPTS ════════
            ["rcore_gangs"]          = "rcore_gangs: Requires extensive zone mapping. Disable default qb-gangs system to avoid conflict.",
            ["rcore_prison"]         = "rcore_prison: Replaces default jail scripts. Ensure jail/unjail commands are properly routed in your framework.",
            ["rcore_arcade"]         = "rcore_arcade: Requires CEF (Chromium Embedded Framework) enabled on the client. Test NUI functionality after install.",

            // ════════ FUEL SYSTEMS ════════
            ["ox_fuel"]              = "ox_fuel: Replaces legacy fuel scripts. Ensure all gas station MLOs are compatible with the new target zones.",
            ["ps-fuel"]              = "ps-fuel: Requires setting up nozzle models. Disable default framework fuel scripts before starting.",
            ["LegacyFuel"]           = "[DEPRECATED] LegacyFuel: Causes sync desync on modern builds. Migrate to ox_fuel or ps-fuel.",

            // ════════ HOUSING & DOORS ════════
            ["ps-housing"]           = "ps-housing: Requires an interior props resource (e.g., K4MB1) to be streamed for furniture placement.",
            ["qs-housing"]           = "qs-housing: Disable default framework housing. Check routing bucket logic for garage conflicts.",
            ["jp-motels"]            = "jp-motels: Requires routing bucket setup in server.cfg to handle interior instancing correctly.",
            ["ox_doorlock"]          = "ox_doorlock: Highly optimised. Replaces qb-doorlock and nui_doorlock. Run the migration tool if upgrading.",
            ["qb-doorlock"]          = "[LEGACY] qb-doorlock: Consider upgrading to ox_doorlock for better performance and stability.",

            // ════════ MODERN INFRASTRUCTURE ════════
            ["renewed-banking"]      = "renewed-banking: Requires ox_lib. Disable qb-banking/esx_banking — running both causes transaction duplication.",
            ["xdope_vangelico"]      = "xdope_vangelico: Requires specific minigame dependencies (e.g., ps-ui, memorygame). Verify all exports exist.",

            // ════════ ENVIRONMENT & MAPPING ════════
            ["bob74_ipl"]            = "bob74_ipl: Must start BEFORE other map resources to ensure default GTA V interiors load correctly.",
            ["vSync"]                = "vSync: Conflicts with modern framework weather systems. Choose exactly one weather resource.",
            ["cd_easytime"]          = "cd_easytime: Replaces vSync and qb-weathersync. Disable those before running this.",
            ["PolyZone"]             = "PolyZone: Must start before any resource using CreateCircleZone or CreateBoxZone.",
            ["spawnmanager"]         = "spawnmanager: Must start after the framework. Check server.cfg start order.",
            ["mapmanager"]           = "mapmanager: Core FiveM resource. Do not remove or modify its start order.",
            ["sessionmanager"]       = "sessionmanager: Controls player connections. Removing it may break player syncing.",
            ["basic-gamemode"]       = "basic-gamemode: Placeholder resource. Remove or replace in any production environment.",
            ["hardcap"]              = "hardcap: Enforces max player count. Ensure the value matches your server license slot count.",

            // ════════ JPRESOURCES (JPR) ════════
            ["jpr-libs"]             = "jpr-libs: Core dependency for ALL JPR scripts. Must start BEFORE jpr-phonesystem, jpr-garages, jpr-mdtsystem, etc.",
            ["jpr-phonesystem"]      = "jpr-phonesystem: Disable your framework's default phone. Requires valid webhook setup for camera functionality.",
            ["jpr-inventory"]        = "jpr-inventory: Disable default framework inventories before starting. Ensure SQL schema is imported.",
            ["jpr-garages"]          = "jpr-garages: Disable default framework garages (qb-garages, etc.). Ensure Qbox/QBCore vehicle tables are synced.",
            ["jpr-mdtsystem"]        = "jpr-mdtsystem: ⚠️ CONFLICT RISK — Only run ONE MDT system (jpr-mdtsystem, xd_mdt, or redutzu-mdt). Requires precise SQL schema for charges and warrants.",
            ["jpr-mechanic"]         = "jpr-mechanic: ⚠️ CONFLICT RISK — Conflicts with jg-mechanic and xd_freetuner. Ensure only ONE mechanic/tuning script is active.",
            ["jpr-policejob"]        = "jpr-policejob: Ensure framework job names strictly match the config definitions. Disable qb-policejob.",

            // ════════ XDOPE (XD) ════════
            ["xd_lib"]               = "xd_lib: Core dependency for ALL xDope scripts. Must start at the top of server.cfg before any xd_* resource.",
            ["xd_multicharacter"]    = "xd_multicharacter: Disable default framework character selection (qb-multicharacter). Qbox users ensure identity bridging is active.",
            ["xd_ambulancejob"]      = "xd_ambulancejob: Replaces default EMS systems. MUST completely disable qb-ambulancejob or esx_ambulancejob.",
            ["xd_chopshop"]          = "xd_chopshop: ⚠️ CONFLICT RISK — Check polyzones for overlap with lation_chopshop if both are installed. Pick one.",
            ["xd_racing"]            = "xd_racing: Requires specific SQL tables for track saving and ELO. Ensure oxmysql connection is stable.",
            ["xd_hud"]               = "xd_hud: Disable all default HUDs (qb-hud, codem-hud). Qbox users ensure hunger/thirst metadata is correctly bridged.",
            ["xd_dealerships"]       = "xd_dealerships: Vehicles must be defined in the xd_dealerships SQL table, not just the framework shared vehicle list.",
            ["xd_mdt"]               = "xd_mdt: ⚠️ CONFLICT RISK — Only run ONE MDT system (xd_mdt, jpr-mdtsystem, or redutzu-mdt). Run the provided SQL before first start.",

            // ════════ LATION SCRIPTS (EXTENDED) ════════
            ["lation_ui"]            = "lation_ui: Standalone notification/UI replacement. Ensure 'Config.Notify' or 'Config.UI' in dependent scripts point to Lation exports.",
            ["lation_coke"]          = "lation_coke: ⚠️ CONFLICT RISK — Ensure processing locations don't overlap with xd_cocaine if both are installed.",
            ["lation_labs"]          = "lation_labs: Requires lation_core started first. Verify item names match your ox_inventory or qb-inventory definitions.",

            // ════════ JG SCRIPTS (EXTENDED) ════════
            ["jg-mechanic"]          = "jg-mechanic: ⚠️ CONFLICT RISK — Requires jg-mechanic-props. Conflicts with jpr-mechanic and onx-tuning. Choose only ONE mechanic script.",
            ["jg-vehiclemileage"]    = "jg-vehiclemileage: Ensure the SQL table for mileage tracking is imported to prevent errors on vehicle entry.",

            // ════════ RAHE SCRIPTS ════════
            ["rahe-boosting"]        = "rahe-boosting: Requires specific hacking device items registered in ox_inventory. Check item name casing.",
            ["rahe-hackingdevice"]   = "rahe-hackingdevice: Ensure the minigame NUI does not conflict with ps-ui or xd_minigames.",
            ["rahe-audio"]           = "rahe-audio: Required for rahe-speakers. Ensure it does not conflict with xsound or rm_stream.",

            // ════════ KQ SCRIPTS (KuzQuality) ════════
            ["kq_brakeoverheat"]     = "kq_brakeoverheat: High tick-rate on client threads. Ensure vehicle meta handling values don't conflict with its brake calculations.",
            ["kq_realoffroad"]       = "kq_realoffroad: Modifies vehicle traction dynamically. Test extensively alongside xd_handling — both modify the same native properties.",
            ["kq_driftsmoke"]        = "kq_driftsmoke: Can cause FPS drops on lower-end client PCs due to particle spawning. Consider setting spawn limits in config.",

            // ════════ WASABI SCRIPTS (EXTENDED) ════════
            ["wasabi_bridge"]        = "wasabi_bridge: Core requirement for modern Wasabi scripts. Fully compatible with Qbox — ensure Config.Framework is set to 'qbx'.",
            ["wasabi_gangwars"]      = "wasabi_gangwars: Ensure gang definitions exactly match your Qbox/QBCore gang configurations in shared data.",

            // ════════ OTHER NOTABLE DETECTIONS ════════
            ["qbx_core"]             = "qbx_core (Qbox): Ensure ox_lib AND oxmysql are started BEFORE this resource. Do NOT run qb-core alongside unless strictly using the compatibility bridge.",
            ["redutzu-mdt"]          = "redutzu-mdt: ⚠️ CONFLICT RISK — Comprehensive MDT. Requires redutzu-mdt-prop and SQL setup. Pick ONLY ONE MDT (xd_mdt, jpr-mdtsystem, or redutzu-mdt).",
            ["rcore_radiocar"]       = "rcore_radiocar: Check for UI conflicts with your active HUD. Requires xsound or a compatible audio routing script.",
            ["onx-tuning"]           = "onx-tuning: High-end tuning script. ⚠️ CONFLICT RISK — Do not run alongside jg-mechanic or jpr-mechanic simultaneously.",
            ["boii_utils"]           = "boii_utils: Core library for all BOII scripts. Must start before boii_chat or any other boii_* resources.",
        };

        // ─── Deprecated patterns ─────────────────────────────────────────────────

        private static readonly HashSet<string> DeprecatedFiles = new(StringComparer.OrdinalIgnoreCase)
        {
            "__resource.lua", "citizen/scripting/lua/scheduler.lua"
        };

        private static readonly Regex DeprecatedFuncRegex = new(
            @"\b(AddEventHandler|TriggerEvent|RegisterServerEvent)\b",
            RegexOptions.Compiled);

        // ─── Public API ──────────────────────────────────────────────────────────

        public static async Task<LinterResult> RunLinterAsync(
            IManifestProvider provider,
            string            rootPath,
            IProgress<int>?   progress,
            LogWriter?        log)
        {
            var result = new LinterResult();
            log?.LogWrite($"[LINTER] Starting scan of: {rootPath}");

            // 1. Gather all manifests
            var manifests = await provider.GetManifestsAsync(rootPath);
            result.ResourcesScanned = manifests.Count;
            log?.LogWrite($"[LINTER] Found {manifests.Count} resource(s).");

            // 2. Gather stream file map for conflict detection
            var streamMap = await provider.GetStreamFileMapAsync(rootPath);

            // 3. Lint each resource
            for (int i = 0; i < manifests.Count; i++)
            {
                var (resourceName, content) = manifests[i];
                bool hadIssue = false;

                // Track every scanned resource name for the Auto-Wirer dependency graph
                result.InstalledResources.Add(resourceName);

                hadIssue |= LintDeprecatedManifest(resourceName, content, result);
                hadIssue |= LintMissingFxVersion(resourceName, content, result);
                hadIssue |= LintMissingGame(resourceName, content, result);
                hadIssue |= LintEmptyFilesBlock(resourceName, content, result);
                hadIssue |= LintKnownIntegrations(resourceName, content, result);
                hadIssue |= LintDeprecatedFunctions(resourceName, content, result);

                if (hadIssue) result.ResourcesWithIssues++;

                progress?.Report((i + 1) * 100 / manifests.Count);
            }

            // 4. Cross-resource stream conflict detection
            LintStreamConflicts(streamMap, result);

            // 5. Sort: Critical first, then Warning, then Info (descending severity)
            result.Warnings.Sort((a, b) => b.Severity.CompareTo(a.Severity));

            log?.LogWrite($"[LINTER] Done. {result.Warnings.Count} issue(s) found across {result.ResourcesWithIssues} resource(s).");
            return result;
        }

        // ─── Lint rules ──────────────────────────────────────────────────────────

        private static bool LintDeprecatedManifest(string name, string content, LinterResult result, string? filePath = null)
        {
            // Using __resource.lua is deprecated since FiveM SDK v2
            if (!content.Contains("fx_version") && content.Contains("resource_manifest_version"))
            {
                result.Warnings.Add(new LinterWarning
                {
                    ResourceName = name,
                    Message      = "Uses legacy __resource.lua format. Migrate to fxmanifest.lua with fx_version 'cerulean'.",
                    Severity     = Severity.Warning
                });
                // Track the path so the AutoFixer can act on it
                if (filePath != null)
                    result.DeprecatedManifestPaths.Add(filePath);
                return true;
            }
            return false;
        }

        private static bool LintMissingFxVersion(string name, string content, LinterResult result)
        {
            if (!content.Contains("fx_version")) return false; // Only check fxmanifest files

            if (!Regex.IsMatch(content, @"fx_version\s+'"))
            {
                result.Warnings.Add(new LinterWarning
                {
                    ResourceName = name,
                    Message      = "Missing fx_version declaration. Add: fx_version 'cerulean'",
                    Severity     = Severity.Critical
                });
                return true;
            }
            return false;
        }

        private static bool LintMissingGame(string name, string content, LinterResult result)
        {
            if (!content.Contains("fx_version")) return false;

            if (!Regex.IsMatch(content, @"game\s+'gta5'"))
            {
                result.Warnings.Add(new LinterWarning
                {
                    ResourceName = name,
                    Message      = "Missing game declaration. Add: game 'gta5'",
                    Severity     = Severity.Critical
                });
                return true;
            }
            return false;
        }

        private static bool LintEmptyFilesBlock(string name, string content, LinterResult result)
        {
            // Detect empty files {} blocks which produce a confusing warning in FiveM logs
            if (Regex.IsMatch(content, @"\bfiles\s*\{\s*\}"))
            {
                result.Warnings.Add(new LinterWarning
                {
                    ResourceName = name,
                    Message      = "Empty files {} block detected. Remove it or add required NUI files.",
                    Severity     = Severity.Info
                });
                return true;
            }
            return false;
        }

        private static bool LintKnownIntegrations(string name, string content, LinterResult result)
        {
            bool found = false;
            foreach (var (resource, hint) in KnownIntegrations)
            {
                if (content.Contains(resource, StringComparison.OrdinalIgnoreCase))
                {
                    result.Warnings.Add(new LinterWarning
                    {
                        ResourceName = name,
                        Message      = $"Integration hint — {resource}: {hint}",
                        Severity     = Severity.Info
                    });
                    found = true;
                }
            }
            return found;
        }

        private static bool LintDeprecatedFunctions(string name, string content, LinterResult result)
        {
            var matches = DeprecatedFuncRegex.Matches(content);
            if (matches.Count == 0) return false;

            var funcs = matches.Cast<Match>()
                               .Select(m => m.Value)
                               .Distinct()
                               .ToList();

            result.Warnings.Add(new LinterWarning
            {
                ResourceName = name,
                Message      = $"Deprecated function(s) detected: {string.Join(", ", funcs)}. Migrate to modern equivalents.",
                Severity     = Severity.Warning
            });
            return true;
        }

        private static void LintStreamConflicts(
            Dictionary<string, List<string>> streamMap,
            LinterResult                      result)
        {
            // Build reverse map: filename → list of resources that declare it
            var fileToResources = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var (resource, files) in streamMap)
            {
                foreach (var file in files)
                {
                    if (!fileToResources.TryGetValue(file, out var list))
                        fileToResources[file] = list = new List<string>();
                    list.Add(resource);
                }
            }

            foreach (var (file, resources) in fileToResources)
            {
                if (resources.Count < 2) continue;
                result.Warnings.Add(new LinterWarning
                {
                    ResourceName = string.Join(", ", resources),
                    Message      = $"Stream conflict: '{file}' is declared in {resources.Count} resources. Only one will load.",
                    Severity     = Severity.Critical
                });
            }
        }
    }
}
