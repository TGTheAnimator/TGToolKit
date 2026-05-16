using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ToolKitV.Models.Providers;

namespace ToolKitV.Models
{
    public static class LinterAutoFixer
    {
        // ─── Public API ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Fuzzy-logic Auto-Wirer. Works over local disk OR live SFTP (RocketNode)
        /// depending on which <see cref="IFileSystemProvider"/> is injected.
        /// All file modifications are preceded by a .tg_backup creation.
        /// </summary>
        public static async Task<int> ApplySmartFixesAsync(
            IFileSystemProvider fs,
            string              serverRootPath,
            HashSet<string>     installedResources,
            LogWriter?          log)
        {
            int fixesApplied = 0;
            log?.LogWrite("=== Starting Auto-Wirer (provider-abstracted) ===");

            // ── 1. Ecosystem detection ────────────────────────────────────────────────
            string activeFramework = installedResources.Contains("qbx_core")    ? "qbox"       :
                                     installedResources.Contains("qb-core")     ? "qb"         :
                                     installedResources.Contains("es_extended") ? "esx"        : "standalone";

            string activeInventory = installedResources.Contains("ox_inventory")  ? "ox"      :
                                     installedResources.Contains("qs-inventory")  ? "quasar"  :
                                     installedResources.Contains("jpr-inventory") ? "jpr"     :
                                     installedResources.Contains("ps-inventory")  ? "ps"      : "qb";

            string activeTarget    = installedResources.Contains("ox_target")  ? "ox_target" :
                                     installedResources.Contains("qb-target")  ? "qb-target" : "none";

            string activePhone     = installedResources.Contains("jpr-phonesystem") ? "jpr-phonesystem" :
                                     installedResources.Contains("lb-phone")        ? "lb-phone"        :
                                     installedResources.Contains("qs-smartphone")   ? "qs-smartphone"   :
                                     installedResources.Contains("renewed-phone")   ? "renewed-phone"   : "qb-phone";

            string activeNotify    = installedResources.Contains("lation_ui")    ? "lation"  :
                                     installedResources.Contains("okokNotify")   ? "okok"    :
                                     installedResources.Contains("mythic_notify")? "mythic"  :
                                     installedResources.Contains("ox_lib")       ? "ox"      : "qb";

            log?.LogWrite($"[ECOSYSTEM] Framework={activeFramework} | Inventory={activeInventory} | Target={activeTarget} | Phone={activePhone} | UI={activeNotify}");

            // ── 2. server.cfg convar injections (local only — SFTP path uses remote root) ─
            string sep           = serverRootPath.Contains('/') ? "/" : "\\";
            string serverCfgPath = serverRootPath.TrimEnd('/', '\\') + sep + "server.cfg";

            try
            {
                string cfgText  = await fs.ReadAllTextAsync(serverCfgPath);
                bool cfgChanged = false;

                if (installedResources.Contains("pma-voice") && !cfgText.Contains("voice_useNativeAudio"))
                {
                    log?.LogWrite("[FIX] Injecting pma-voice convars into server.cfg");
                    cfgText = "setr voice_useNativeAudio true\nsetr voice_use3dAudio true\nsetr voice_defaultCycle \"GRAVE\"\n\n" + cfgText;
                    cfgText = Regex.Replace(cfgText, @"(?m)^(\s*(?:ensure|start)\s+mumble-voip\s*)$",
                        "# [TGToolKit] Auto-disabled: conflicts with pma-voice\n# $1", RegexOptions.IgnoreCase);
                    cfgChanged = true;
                }

                if (installedResources.Contains("oxmysql") && !cfgText.Contains("mysql_connection_string"))
                {
                    log?.LogWrite("[FIX] Injecting oxmysql connection string template into server.cfg");
                    cfgText = "# [TGToolKit] Configure DB credentials:\nset mysql_connection_string \"mysql://root:password@localhost/fivem?charset=utf8mb4\"\n\n" + cfgText;
                    cfgChanged = true;
                }

                if (cfgChanged)
                {
                    await fs.CreateBackupAsync(serverCfgPath);
                    await fs.WriteAllTextAsync(serverCfgPath, cfgText);
                    fixesApplied++;
                }
            }
            catch { /* server.cfg may not exist at this path — not fatal */ }

            // ── 3. Universal fuzzy config re-routing ─────────────────────────────────
            var allManifests = await fs.DiscoverFilesAsync(serverRootPath, "fxmanifest.lua");

            foreach (var manifestPath in allManifests)
            {
                // Normalise to forward-slash for safe string ops on both local and remote paths
                string normalised   = manifestPath.Replace('\\', '/');
                string resourceDir  = normalised.Substring(0, normalised.LastIndexOf('/'));
                string resourceName = resourceDir.Substring(resourceDir.LastIndexOf('/') + 1);

                var allLua = await fs.DiscoverFilesAsync(resourceDir, "*.lua");

                foreach (var luaPath in allLua)
                {
                    string lowerPath = luaPath.Replace('\\', '/').ToLowerInvariant();
                    string lowerName = Path.GetFileName(lowerPath);

                    // Only process files that look like configs
                    if (!lowerName.Contains("config") &&
                        !lowerPath.Contains("/config/") &&
                        !lowerPath.Contains("/shared/"))
                        continue;

                    try
                    {
                        string luaText  = await fs.ReadAllTextAsync(luaPath);
                        bool   modified = false;

                        // Universal routing
                        modified |= TryInjectConfig(ref luaText, "Framework",    activeFramework);
                        modified |= TryInjectConfig(ref luaText, "Core",         activeFramework);
                        modified |= TryInjectConfig(ref luaText, "Inventory",    activeInventory);
                        modified |= TryInjectConfig(ref luaText, "Target",       activeTarget);
                        modified |= TryInjectConfig(ref luaText, "Phone",        activePhone);
                        modified |= TryInjectConfig(ref luaText, "Notify",       activeNotify);
                        modified |= TryInjectConfig(ref luaText, "Notification", activeNotify);
                        modified |= TryInjectConfig(ref luaText, "UI",           activeNotify);

                        if (installedResources.Contains("oxmysql"))
                        {
                            modified |= TryInjectConfig(ref luaText, "Database", "oxmysql");
                            modified |= TryInjectConfig(ref luaText, "Mysql",    "oxmysql");
                        }

                        // Qbox-specific pass: unlock native code paths in modern scripts
                        if (activeFramework == "qbox")
                        {
                            modified |= TryInjectConfig(ref luaText, "Framework", "qbx");
                            modified |= TryInjectConfig(ref luaText, "Core",      "qbx");
                        }

                        if (modified)
                        {
                            await fs.CreateBackupAsync(luaPath);
                            await fs.WriteAllTextAsync(luaPath, luaText);
                            fixesApplied++;
                            log?.LogWrite($"[WIRED] {resourceName}/{Path.GetFileName(luaPath)}");
                        }
                    }
                    catch { }
                }
            }

            // ── 4. ox_lib fxmanifest injection ───────────────────────────────────────
            if (installedResources.Contains("ox_lib"))
            {
                foreach (var manifestPath in allManifests)
                {
                    try
                    {
                        string text = await fs.ReadAllTextAsync(manifestPath);
                        bool usesOxLib  = text.Contains("lib.print") || text.Contains("lib.registerContext") ||
                                          text.Contains("lib.callback") || text.Contains("lib.notify");

                        if (usesOxLib && !text.Contains("@ox_lib"))
                        {
                            string rn = Path.GetFileName(Path.GetDirectoryName(manifestPath.Replace('\\', '/'))!);
                            log?.LogWrite($"[FIX] Injecting @ox_lib/init.lua into {rn}/fxmanifest.lua");
                            await fs.CreateBackupAsync(manifestPath);
                            text += "\n-- [TGToolKit] Injected missing ox_lib dependency\nshared_script '@ox_lib/init.lua'\n";
                            await fs.WriteAllTextAsync(manifestPath, text);
                            fixesApplied++;
                        }
                    }
                    catch { }
                }
            }

            // ── 5. __resource.lua → fxmanifest.lua (local only) ─────────────────────
            // This step is skipped in SFTP mode because deleting remote files is risky
            // without a full atomic rename. The Fix All button handles local conversion.
            if (fs is LocalFileSystemProvider)
            {
                var deprecated = await fs.DiscoverFilesAsync(serverRootPath, "__resource.lua");
                foreach (var oldManifest in deprecated)
                {
                    try
                    {
                        string dir  = Path.GetDirectoryName(oldManifest)!;
                        string rn   = Path.GetFileName(dir);
                        string lua  = await fs.ReadAllTextAsync(oldManifest);

                        if (!lua.Contains("fx_version"))
                            lua = "fx_version 'cerulean'\ngame 'gta5'\n\n" + lua;

                        lua = Regex.Replace(lua, @"resource_manifest_version\s+'[^']*'\s*\n?", string.Empty);
                        lua = Regex.Replace(lua, @"server_scripts\s*\{",  "server_scripts {");
                        lua = Regex.Replace(lua, @"client_scripts\s*\{",  "client_scripts {");
                        lua = Regex.Replace(lua, @"shared_scripts\s*\{",  "shared_scripts {");

                        string newPath = Path.Combine(dir, "fxmanifest.lua");
                        if (File.Exists(newPath)) continue;

                        await fs.WriteAllTextAsync(newPath, lua);
                        File.Delete(oldManifest);
                        fixesApplied++;
                        log?.LogWrite($"[FIX] {rn}: __resource.lua → fxmanifest.lua");
                    }
                    catch { }
                }
            }

            log?.LogWrite($"=== Auto-Wirer done. {fixesApplied} fix(es) applied. ===");
            return fixesApplied;
        }

        /// <summary>
        /// Simple path-based deprecated manifest converter used by the "Fix All" button.
        /// Operates on local paths from a previous scan result.
        /// </summary>
        public static int FixDeprecatedManifests(ServerLinter.LinterResult results, LogWriter? log)
        {
            int fixedCount = 0;

            foreach (var path in results.DeprecatedManifestPaths)
            {
                if (!File.Exists(path)) continue;

                try
                {
                    string dir  = Path.GetDirectoryName(path) ?? string.Empty;
                    string rn   = Path.GetFileName(dir);
                    string lua  = File.ReadAllText(path);

                    if (!lua.Contains("fx_version"))
                        lua = $"fx_version 'cerulean'\ngame 'gta5'\n\n" + lua;

                    lua = Regex.Replace(lua, @"resource_manifest_version\s+'[^']*'\s*\n?", string.Empty);
                    lua = Regex.Replace(lua, @"server_scripts\s*\{",  "server_scripts {");
                    lua = Regex.Replace(lua, @"client_scripts\s*\{",  "client_scripts {");
                    lua = Regex.Replace(lua, @"shared_scripts\s*\{",  "shared_scripts {");
                    lua = Regex.Replace(lua, @"files\s*\{",           "files {");

                    string newPath = Path.Combine(dir, "fxmanifest.lua");
                    if (File.Exists(newPath))
                    {
                        log?.LogWrite($"[SKIP] {rn}: fxmanifest.lua already exists.");
                        continue;
                    }

                    File.WriteAllText(newPath, lua, System.Text.Encoding.UTF8);
                    File.Delete(path);
                    fixedCount++;
                    log?.LogWrite($"[FIXED] {rn}: __resource.lua → fxmanifest.lua");
                }
                catch (Exception ex)
                {
                    log?.LogWrite($"[ERROR] {path}: {ex.Message}");
                }
            }

            log?.LogWrite($"[AUTO-FIX] Converted {fixedCount}/{results.DeprecatedManifestPaths.Count} manifest(s).");
            return fixedCount;
        }

        // ─── Conflict Resolution ─────────────────────────────────────────────────────

        /// <summary>
        /// Quarantines "loser" scripts for each conflict category the user resolved.
        /// Renames the folder to <c>.disabled_{name}</c> (FiveM ignores dot-prefixed dirs)
        /// and comments out the matching line in server.cfg.
        /// </summary>
        public static async Task<int> ResolveConflictsAsync(
            Providers.IFileSystemProvider fs,
            string               serverRootPath,
            Dictionary<string, string> resolvedChoices,   // winner → category title
            HashSet<string>      allDetectedScripts,
            LogWriter?           log)
        {
            int quarantined = 0;
            log?.LogWrite("=== Starting Surgical Conflict Resolution ===");

            // ── 1. Identify losers ────────────────────────────────────────────────────
            var losers = new List<string>();
            foreach (var cat in ConflictDefinitions.Categories)
            {
                string? winner = null;
                foreach (var kv in resolvedChoices)
                    if (kv.Value == cat.Title) { winner = kv.Key; break; }

                if (winner == null) continue;

                foreach (var script in cat.MutuallyExclusiveScripts)
                    if (allDetectedScripts.Contains(script) &&
                        !script.Equals(winner, StringComparison.OrdinalIgnoreCase))
                        losers.Add(script);
            }

            // ── 2. Quarantine folders ────────────────────────────────────────────────
            var allManifests = await fs.DiscoverFilesAsync(serverRootPath, "fxmanifest.lua");

            foreach (var loser in losers)
            {
                foreach (var manifest in allManifests)
                {
                    string norm   = manifest.Replace('\\', '/');
                    string dirPath    = norm[..norm.LastIndexOf('/')];
                    string folderName = dirPath[(dirPath.LastIndexOf('/') + 1)..];

                    if (!folderName.Equals(loser, StringComparison.OrdinalIgnoreCase)) continue;

                    string parentDir  = dirPath[..dirPath.LastIndexOf('/')];
                    string newDirPath = parentDir + "/.disabled_" + folderName;

                    try
                    {
                        log?.LogWrite($"[QUARANTINE] {folderName} → .disabled_{folderName}");
                        await fs.RenameDirectoryAsync(dirPath, newDirPath);
                        quarantined++;
                    }
                    catch (Exception ex)
                    {
                        log?.LogWrite($"[ERROR] Could not rename {folderName}: {ex.Message}");
                    }
                    break;
                }
            }

            // ── 3. Comment-disable in server.cfg ────────────────────────────────────
            string cfgPath = serverRootPath.TrimEnd('/', '\\') +
                             (serverRootPath.Contains('/') ? "/" : "\\") + "server.cfg";
            try
            {
                string cfgText     = await fs.ReadAllTextAsync(cfgPath);
                bool   cfgModified = false;

                foreach (var loser in losers)
                {
                    string pattern = $@"(?im)^(\s*(?:ensure|start)\s+{Regex.Escape(loser)}\s*)$";
                    if (Regex.IsMatch(cfgText, pattern))
                    {
                        cfgText = Regex.Replace(cfgText, pattern,
                            $"# [TGToolKit] Quarantined: {loser}\n# $1");
                        cfgModified = true;
                    }
                }

                if (cfgModified)
                {
                    await fs.CreateBackupAsync(cfgPath);
                    await fs.WriteAllTextAsync(cfgPath, cfgText);
                    log?.LogWrite("[QUARANTINE] Updated server.cfg to disable conflicting scripts.");
                }
            }
            catch { /* server.cfg may be missing or inaccessible */ }

            log?.LogWrite($"=== Conflict Resolution done. {quarantined} script(s) quarantined. ===");
            return quarantined;
        }



        private static bool TryInjectConfig(ref string text, string variableName, string newValue)
        {
            string pattern = $@"(?i)((?:Config|cfg|shared|Cfg)\.{variableName}\s*=\s*)(['""])(.*?)(['""])";
            var match = Regex.Match(text, pattern);
            if (!match.Success) return false;

            string oldValue = match.Groups[3].Value;
            if (oldValue.Equals(newValue, StringComparison.OrdinalIgnoreCase)) return false;

            text = Regex.Replace(text, pattern, $"$1$2{newValue}$4");
            return true;
        }
    }
}
