using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace ToolKitV.Models
{
    /// <summary>
    /// Intelligent DevOps Auto-Wirer for FiveM server configurations.
    /// Operates strictly on the local filesystem with an automatic .tg_backup safety net.
    /// </summary>
    public static class LinterAutoFixer
    {
        // ─── Public API ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Fuzzy-logic smart fixer. Detects the server's core ecosystem (inventory, target,
        /// phone, notifications) and re-routes every script's config files to match.
        /// Also injects server.cfg convars, wires framework pairs, and upgrades deprecated manifests.
        /// </summary>
        public static async Task<int> ApplySmartFixesAsync(
            string          serverRootPath,
            HashSet<string> installedResources,
            LogWriter?      log)
        {
            int fixesApplied = 0;
            log?.LogWrite("=== Starting Fuzzy Logic Config Auto-Wirer ===");

            // ── 1. Determine the server's core ecosystem ─────────────────────────────

            // Framework — Qbox takes priority over legacy QBCore
            string activeFramework = installedResources.Contains("qbx_core")    ? "qbox"       :
                                     installedResources.Contains("qb-core")     ? "qb"         :
                                     installedResources.Contains("es_extended") ? "esx"        : "standalone";

            // Inventory — jpr-inventory sits between qs-inventory and qb in priority
            string activeInventory = installedResources.Contains("ox_inventory")  ? "ox"      :
                                     installedResources.Contains("qs-inventory")  ? "quasar"  :
                                     installedResources.Contains("jpr-inventory") ? "jpr"     :
                                     installedResources.Contains("ps-inventory")  ? "ps"      : "qb";

            string activeTarget    = installedResources.Contains("ox_target")     ? "ox_target"  :
                                     installedResources.Contains("qb-target")     ? "qb-target"  : "none";

            // Phone — jpr-phonesystem is the top priority for JPR ecosystems
            string activePhone     = installedResources.Contains("jpr-phonesystem") ? "jpr-phonesystem" :
                                     installedResources.Contains("lb-phone")        ? "lb-phone"        :
                                     installedResources.Contains("qs-smartphone")   ? "qs-smartphone"   :
                                     installedResources.Contains("renewed-phone")   ? "renewed-phone"   : "qb-phone";

            // Notifications/UI — lation_ui is prioritised for Lation-ecosystem servers
            string activeNotify    = installedResources.Contains("lation_ui")    ? "lation"  :
                                     installedResources.Contains("okokNotify")   ? "okok"    :
                                     installedResources.Contains("mythic_notify")? "mythic"  :
                                     installedResources.Contains("ox_lib")       ? "ox"      : "qb";

            log?.LogWrite($"[ECOSYSTEM] Framework={activeFramework} | Inventory={activeInventory} | Target={activeTarget} | Phone={activePhone} | UI={activeNotify}");

            // ── 2. server.cfg convar injections ──────────────────────────────────────
            string serverCfgPath = Path.Combine(serverRootPath, "server.cfg");
            if (File.Exists(serverCfgPath))
            {
                string cfgText  = await File.ReadAllTextAsync(serverCfgPath);
                bool cfgChanged = false;

                // pma-voice optimal convars
                if (installedResources.Contains("pma-voice") && !cfgText.Contains("voice_useNativeAudio"))
                {
                    log?.LogWrite("[FIX] Injecting pma-voice optimal convars into server.cfg");
                    cfgText = "setr voice_useNativeAudio true\n" +
                              "setr voice_use3dAudio true\n" +
                              "setr voice_defaultCycle \"GRAVE\"\n\n" + cfgText;
                    // Comment-disable conflicting mumble-voip
                    cfgText = Regex.Replace(cfgText,
                        @"(?m)^(\s*(?:ensure|start)\s+mumble-voip\s*)$",
                        "# [TGToolKit] Auto-disabled: conflicts with pma-voice\n# $1",
                        RegexOptions.IgnoreCase);
                    cfgChanged = true;
                }

                // oxmysql connection string placeholder
                if (installedResources.Contains("oxmysql") && !cfgText.Contains("mysql_connection_string"))
                {
                    log?.LogWrite("[FIX] Injecting oxmysql connection string template into server.cfg");
                    cfgText = "# [TGToolKit] Configure your DB credentials below:\n" +
                              "set mysql_connection_string \"mysql://root:password@localhost/fivem?charset=utf8mb4\"\n\n" + cfgText;
                    cfgChanged = true;
                }

                if (cfgChanged)
                {
                    BackupFile(serverCfgPath, log);
                    await File.WriteAllTextAsync(serverCfgPath, cfgText);
                    fixesApplied++;
                }
            }

            // ── 3. Framework pair wiring (qb-core ↔ ox_inventory) ───────────────────
            if (installedResources.Contains("qb-core") && installedResources.Contains("ox_inventory"))
            {
                // Fuzzy search for qb-core's config — it may be in different folder layouts
                string[] candidatePaths =
                {
                    Path.Combine(serverRootPath, "resources", "[qb]",   "qb-core", "shared", "main.lua"),
                    Path.Combine(serverRootPath, "resources", "[core]", "qb-core", "shared", "main.lua"),
                    Path.Combine(serverRootPath, "resources", "qb-core", "shared", "main.lua"),
                };

                foreach (var candidate in candidatePaths)
                {
                    if (!File.Exists(candidate)) continue;

                    string qbConfig = await File.ReadAllTextAsync(candidate);
                    const string pattern = @"(?i)(Config\.Inventory\s*=\s*)(['""])qb\2";

                    if (Regex.IsMatch(qbConfig, pattern))
                    {
                        log?.LogWrite($"[FIX] Wiring qb-core → ox_inventory in {candidate}");
                        BackupFile(candidate, log);
                        qbConfig = Regex.Replace(qbConfig, pattern, "$1$2ox$2");
                        await File.WriteAllTextAsync(candidate, qbConfig);
                        fixesApplied++;
                    }
                    break; // Only process the first found path
                }
            }

            // ── 4. Universal fuzzy config re-routing across all resources ─────────────
            var allManifests = Directory.GetFiles(serverRootPath, "fxmanifest.lua", SearchOption.AllDirectories);
            foreach (var manifestPath in allManifests)
            {
                string resourceDir  = Path.GetDirectoryName(manifestPath)!;
                string resourceName = Path.GetFileName(resourceDir);
                var    configFiles  = GetLikelyConfigFiles(resourceDir);

                foreach (var cfgPath in configFiles)
                {
                    try
                    {
                        string luaText  = await File.ReadAllTextAsync(cfgPath);
                        bool   modified = false;

                        // ── Universal Variable Routing ──────────────────────────────
                        modified |= TryInjectConfig(ref luaText, "Framework",    activeFramework);
                        modified |= TryInjectConfig(ref luaText, "Core",         activeFramework); // Config.Core variant
                        modified |= TryInjectConfig(ref luaText, "Inventory",    activeInventory);
                        modified |= TryInjectConfig(ref luaText, "Target",       activeTarget);
                        modified |= TryInjectConfig(ref luaText, "Phone",        activePhone);
                        modified |= TryInjectConfig(ref luaText, "Notify",       activeNotify);
                        modified |= TryInjectConfig(ref luaText, "Notification", activeNotify);
                        modified |= TryInjectConfig(ref luaText, "UI",           activeNotify); // Config.UI = 'lation'

                        if (installedResources.Contains("oxmysql"))
                        {
                            modified |= TryInjectConfig(ref luaText, "Database", "oxmysql");
                            modified |= TryInjectConfig(ref luaText, "Mysql",    "oxmysql");
                        }

                        // ── Qbox-Specific Pass ──────────────────────────────────────
                        // Modern scripts (JG, Wasabi, XDope) expose a 'qbx' identifier
                        // that unlocks native Qbox code paths. Apply only when Qbox is confirmed.
                        if (activeFramework == "qbox")
                        {
                            modified |= TryInjectConfig(ref luaText, "Framework", "qbx");
                            modified |= TryInjectConfig(ref luaText, "Core",      "qbx");
                        }

                        if (modified)
                        {
                            BackupFile(cfgPath, log);
                            await File.WriteAllTextAsync(cfgPath, luaText);
                            fixesApplied++;
                            log?.LogWrite($"[WIRED] Re-routed settings in {resourceName}/{Path.GetFileName(cfgPath)}");
                        }
                    }
                    catch { /* Skip locked or binary-corrupt files */ }
                }
            }

            // ── 5. ox_lib shared_script injection into fxmanifests that use it ────────
            if (installedResources.Contains("ox_lib"))
            {
                foreach (var manifestPath in allManifests)
                {
                    try
                    {
                        string text = await File.ReadAllTextAsync(manifestPath);

                        // Only inject if the resource uses ox_lib exports but hasn't declared the init script
                        bool usesOxLib  = text.Contains("lib.print") || text.Contains("lib.registerContext") ||
                                          text.Contains("lib.callback") || text.Contains("lib.notify");
                        bool alreadySet = text.Contains("@ox_lib");

                        if (usesOxLib && !alreadySet)
                        {
                            string resourceName = Path.GetFileName(Path.GetDirectoryName(manifestPath)!);
                            log?.LogWrite($"[FIX] Injecting missing @ox_lib/init.lua into {resourceName}/fxmanifest.lua");
                            BackupFile(manifestPath, log);
                            text += "\n-- [TGToolKit] Injected missing ox_lib dependency\nshared_script '@ox_lib/init.lua'\n";
                            await File.WriteAllTextAsync(manifestPath, text);
                            fixesApplied++;
                        }
                    }
                    catch { }
                }
            }

            // ── 6. Deprecated __resource.lua → fxmanifest.lua conversion ────────────
            var deprecatedFiles = Directory.GetFiles(serverRootPath, "__resource.lua", SearchOption.AllDirectories);
            foreach (var oldManifest in deprecatedFiles)
            {
                try
                {
                    string resourceName = Path.GetFileName(Path.GetDirectoryName(oldManifest)!);
                    log?.LogWrite($"[FIX] Upgrading __resource.lua → fxmanifest.lua in {resourceName}");

                    string luaText = await File.ReadAllTextAsync(oldManifest);

                    if (!luaText.Contains("fx_version"))
                        luaText = "fx_version 'cerulean'\ngame 'gta5'\n\n" + luaText;

                    luaText = Regex.Replace(luaText, @"resource_manifest_version\s+'[^']*'\s*\n?", string.Empty);
                    luaText = Regex.Replace(luaText, @"server_scripts\s*\{", "server_scripts {");
                    luaText = Regex.Replace(luaText, @"client_scripts\s*\{", "client_scripts {");
                    luaText = Regex.Replace(luaText, @"shared_scripts\s*\{", "shared_scripts {");

                    string newPath = Path.Combine(Path.GetDirectoryName(oldManifest)!, "fxmanifest.lua");
                    if (File.Exists(newPath)) continue; // Don't overwrite if already migrated

                    await File.WriteAllTextAsync(newPath, luaText, System.Text.Encoding.UTF8);
                    File.Delete(oldManifest);
                    fixesApplied++;
                }
                catch { }
            }

            log?.LogWrite($"=== Auto-Wirer Finished. Applied {fixesApplied} intelligent fix(es). ===");
            return fixesApplied;
        }

        /// <summary>
        /// Path-based deprecated manifest converter. Used by the simple "Fix All" button
        /// when operating on a set of already-identified paths from a previous scan.
        /// </summary>
        public static int FixDeprecatedManifests(ServerLinter.LinterResult results, LogWriter? log)
        {
            int fixedCount = 0;

            foreach (var path in results.DeprecatedManifestPaths)
            {
                if (!File.Exists(path)) continue;

                try
                {
                    string directory    = Path.GetDirectoryName(path) ?? string.Empty;
                    string luaText      = File.ReadAllText(path);
                    string resourceName = Path.GetFileName(directory);

                    if (!luaText.Contains("fx_version"))
                        luaText = $"fx_version 'cerulean'\ngame 'gta5'\n\n" + luaText;

                    luaText = Regex.Replace(luaText, @"resource_manifest_version\s+'[^']*'\s*\n?", string.Empty);
                    luaText = Regex.Replace(luaText, @"server_scripts\s*\{", "server_scripts {");
                    luaText = Regex.Replace(luaText, @"client_scripts\s*\{", "client_scripts {");
                    luaText = Regex.Replace(luaText, @"shared_scripts\s*\{", "shared_scripts {");
                    luaText = Regex.Replace(luaText, @"files\s*\{",          "files {");

                    string newPath = Path.Combine(directory, "fxmanifest.lua");
                    if (File.Exists(newPath))
                    {
                        log?.LogWrite($"[SKIP] {resourceName}: fxmanifest.lua already exists.");
                        continue;
                    }

                    File.WriteAllText(newPath, luaText, System.Text.Encoding.UTF8);
                    File.Delete(path);
                    fixedCount++;
                    log?.LogWrite($"[FIXED] {resourceName}: __resource.lua → fxmanifest.lua");
                }
                catch (Exception ex)
                {
                    log?.LogWrite($"[ERROR] Could not fix {path}: {ex.Message}");
                }
            }

            log?.LogWrite($"[AUTO-FIX] Converted {fixedCount}/{results.DeprecatedManifestPaths.Count} deprecated manifest(s).");
            return fixedCount;
        }

        // ─── Private helpers ─────────────────────────────────────────────────────────

        /// <summary>
        /// Hunts for any Lua file that is likely a configuration file:
        /// name contains "config", or lives inside a /config/ or /shared/ subfolder.
        /// </summary>
        private static List<string> GetLikelyConfigFiles(string resourceDir)
        {
            var results    = new List<string>();
            var allLuaFiles = Directory.GetFiles(resourceDir, "*.lua", SearchOption.AllDirectories);

            foreach (var file in allLuaFiles)
            {
                string fileName = Path.GetFileName(file).ToLowerInvariant();
                string filePath = file.Replace('\\', '/').ToLowerInvariant();

                if (fileName.Contains("config") ||
                    filePath.Contains("/config/") ||
                    filePath.Contains("/shared/"))
                {
                    results.Add(file);
                }
            }

            return results;
        }

        /// <summary>
        /// Uses Regex to locate a variable like <c>Config.Inventory = 'qb'</c> and replaces
        /// its value with <paramref name="newValue"/>, preserving the original quote style and spacing.
        /// Handles <c>Config.</c>, <c>cfg.</c>, and <c>Shared.</c> table prefixes.
        /// </summary>
        private static bool TryInjectConfig(ref string text, string variableName, string newValue)
        {
            // Matches: Config.Inventory = "qb" / cfg.target = 'qb-target' / Shared.Notify   =  "okok"
            string pattern = $@"(?i)((?:Config|cfg|shared|Cfg)\.{variableName}\s*=\s*)(['""])(.*?)(['""])";

            var match = Regex.Match(text, pattern);
            if (!match.Success) return false;

            string oldValue = match.Groups[3].Value;
            if (oldValue.Equals(newValue, StringComparison.OrdinalIgnoreCase)) return false;

            // $1 = prefix+equals, $2 = opening quote, newValue, $4 = closing quote (preserves style)
            text = Regex.Replace(text, pattern, $"$1$2{newValue}$4");
            return true;
        }

        /// <summary>
        /// Copies the file to <c>filename.tg_backup</c> before any modification.
        /// Skips silently if a backup already exists (idempotent).
        /// </summary>
        private static void BackupFile(string originalPath, LogWriter? log = null)
        {
            try
            {
                string backupPath = originalPath + ".tg_backup";
                if (!File.Exists(backupPath))
                {
                    File.Copy(originalPath, backupPath);
                    log?.LogWrite($"[BACKUP] {Path.GetFileName(originalPath)} → {Path.GetFileName(backupPath)}");
                }
            }
            catch { /* Best-effort — never block a fix due to a backup failure */ }
        }
    }
}
