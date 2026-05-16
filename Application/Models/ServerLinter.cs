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
            public int                  ResourcesScanned  { get; set; }
            public int                  ResourcesWithIssues { get; set; }
            public List<LinterWarning>  Warnings          { get; set; } = new();
        }

        // ─── Known integration hints ─────────────────────────────────────────────

        private static readonly Dictionary<string, string> KnownIntegrations =
            new(StringComparer.OrdinalIgnoreCase)
        {
            ["ox_inventory"]        = "ox_inventory requires items registered in data/items.lua and ox_inventory started before dependent resources.",
            ["ox_lib"]              = "ox_lib must be started before any resource using @ox_lib/init.lua.",
            ["ox_target"]           = "ox_target requires ox_lib. Ensure ox_lib starts first.",
            ["qb-core"]             = "qb-core must be the first framework resource. Check server.cfg start order.",
            ["qbx_core"]            = "qbx_core (Qbox) replaces qb-core. Do not run both simultaneously.",
            ["es_extended"]         = "es_extended (ESX) must start before any esx_* dependent resources.",
            ["PolyZone"]            = "PolyZone must start before any resource using CreateCircleZone or CreateBoxZone.",
            ["interact-sound"]      = "interact-sound requires NUI callbacks. Ensure no other resource blocks NUI on this endpoint.",
            ["pma-voice"]           = "pma-voice requires mumble-voip disabled in server.cfg and a valid voice server configured.",
            ["screenshot-basic"]    = "screenshot-basic requires the Discord webhook URL set and a compatible game build for canvas capturing.",
            ["progressbar"]         = "progressbar needs HTML/CSS files intact. If UI doesn't show, check NUI callbacks are not blocked.",
            ["mysql-async"]         = "mysql-async is deprecated. Migrate to oxmysql for improved stability.",
            ["oxmysql"]             = "oxmysql must start before any resource that uses exports.oxmysql.",
            ["hardcap"]             = "hardcap enforces max player count. Ensure the value matches your server license slot count.",
            ["spawnmanager"]        = "spawnmanager should start after the framework. Check server.cfg order.",
            ["mapmanager"]          = "mapmanager is a core FiveM resource. Do not remove or modify its start order.",
            ["sessionmanager"]      = "sessionmanager controls player connections. Removing it may break player syncing.",
            ["basic-gamemode"]      = "basic-gamemode is a placeholder. It should be replaced or removed in production.",
            ["bob74_ipl"]           = "bob74_ipl handles interior proxy loading. Load before any MLO-dependent resources.",
            ["illenium-appearance"] = "illenium-appearance requires cl_enableLargeFetch=1 in server.cfg for large clothing data.",
            ["jg-advancedgarages"]  = "jg-advancedgarages requires database tables. Run its SQL on first install.",
            ["cd_garage"]           = "cd_garage (Codesign) requires database access and the framework started first.",
            ["vSync"]               = "vSync (Visual Sync) requires the correct time/weather zone configuration in config.lua.",
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

        private static bool LintDeprecatedManifest(string name, string content, LinterResult result)
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
