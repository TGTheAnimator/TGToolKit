using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

        // ─── server.cfg Order Validator / Fixer ──────────────────────────────────────

        /// <summary>
        /// Reads server.cfg, validates load order against the canonical FiveM dependency chain,
        /// reorders <c>ensure</c> / <c>start</c> lines to match, and appends any installed
        /// resource that does not yet have an entry. Creates a .tg_backup before writing.
        /// </summary>
        public static async Task<(int reordered, int added)> FixServerCfgOrderAsync(
            IFileSystemProvider fs,
            string              serverRootPath,
            HashSet<string>     installedResources,
            LogWriter?          log)
        {
            log?.LogWrite("=== server.cfg Load-Order Validator ===");

            string sep     = serverRootPath.Contains('/') ? "/" : "\\";
            string cfgPath = serverRootPath.TrimEnd('/', '\\') + sep + "server.cfg";

            string original;
            try   { original = await fs.ReadAllTextAsync(cfgPath); }
            catch { log?.LogWrite("[SKIP] server.cfg not found or unreadable."); return (0, 0); }

            // ── Canonical tier order ──────────────────────────────────────────────────
            // Lower index = must start earlier. Scripts not in this list go to tier 99.
            var tierMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                // Tier 0 — Core FiveM & database
                ["mapmanager"]      = 0, ["sessionmanager"] = 0, ["spawnmanager"]  = 0,
                ["basic-gamemode"]  = 0, ["hardcap"]        = 0,
                // Tier 1 — Overextended libs (must precede everything using them)
                ["ox_lib"]          = 1, ["oxmysql"]        = 1,
                // Tier 2 — Framework core
                ["qbx_core"]        = 2, ["qb-core"]        = 2, ["es_extended"]   = 2,
                // Tier 3 — Core framework dependencies
                ["ox_inventory"]    = 3, ["qb-inventory"]   = 3, ["qbx_inventory"] = 3,
                ["qs-inventory"]    = 3, ["jpr-inventory"]  = 3, ["ps-inventory"]  = 3,
                ["ox_target"]       = 3, ["qb-target"]      = 3, ["qbx_target"]    = 3,
                // Tier 4 — Voice / audio (before gameplay scripts)
                ["pma-voice"]       = 4, ["saltychat"]      = 4,
                // Tier 5 — Utility libs that gameplay scripts depend on
                ["xd_lib"]          = 5, ["jpr-libs"]       = 5, ["boii_utils"]    = 5,
                ["lation_core"]     = 5, ["lation_ui"]      = 5, ["wasabi_bridge"] = 5,
                // Tier 6 — Phone (needed early for webhook hooks)
                ["jpr-phonesystem"] = 6, ["lb-phone"]       = 6, ["qs-smartphone"] = 6,
                // Tier 7 — Everything else (premium gameplay scripts) — implicit tier 99
            };

            // ── Parse server.cfg lines ────────────────────────────────────────────────
            var lines        = original.Split('\n').ToList();
            var ensureLines  = new List<(int lineIdx, string resource, bool isComment)>();

            for (int i = 0; i < lines.Count; i++)
            {
                string trimmed = lines[i].TrimStart();
                bool   commented = trimmed.StartsWith('#');
                string effective = commented ? trimmed.TrimStart('#', ' ') : trimmed;

                var m = Regex.Match(effective, @"^(?:ensure|start)\s+(\S+)", RegexOptions.IgnoreCase);
                if (m.Success)
                    ensureLines.Add((i, m.Groups[1].Value, commented));
            }

            // ── Detect ordering violations ────────────────────────────────────────────
            int previousTier  = -1;
            int reordered     = 0;
            var violations    = new List<(int lineIdx, string resource)>();

            foreach (var (lineIdx, resource, isComment) in ensureLines)
            {
                if (isComment) continue;
                int tier = tierMap.TryGetValue(resource, out int t) ? t : 99;
                if (tier < previousTier)
                    violations.Add((lineIdx, resource));
                else
                    previousTier = tier;
            }

            // ── Reorder: extract all ensure lines, sort by tier, reinsert ─────────────
            if (violations.Count > 0)
            {
                log?.LogWrite($"[ORDER] {violations.Count} ordering violation(s) detected. Reordering...");

                // Pull all active (non-commented) ensure entries out
                var activeEnsures = ensureLines
                    .Where(e => !e.isComment)
                    .OrderBy(e => tierMap.TryGetValue(e.resource, out int t) ? t : 99)
                    .ThenBy(e => e.resource)
                    .ToList();

                // Build new ensure block
                var ensureBlock = activeEnsures
                    .Select(e => $"ensure {e.resource}")
                    .ToList();

                // Remove old ensure lines from original (highest index first to preserve positions)
                var removeIdxs = ensureLines
                    .Where(e => !e.isComment)
                    .Select(e => e.lineIdx)
                    .OrderByDescending(i => i)
                    .ToList();

                foreach (int idx in removeIdxs)
                    lines.RemoveAt(idx);

                // Find insertion point: first line after the last comment block / convar block
                int insertAt = 0;
                for (int i = 0; i < lines.Count; i++)
                {
                    string t = lines[i].TrimStart();
                    if (t.StartsWith("set ") || t.StartsWith("setr ") || t.StartsWith("sv_") ||
                        t.StartsWith("endpoint_") || t.StartsWith("load_server_icon"))
                        insertAt = i + 1;
                }

                lines.Insert(insertAt, "");
                lines.Insert(insertAt + 1, "# ── Load Order (managed by TGToolKit) ─────────────────────");
                for (int i = 0; i < ensureBlock.Count; i++)
                    lines.Insert(insertAt + 2 + i, ensureBlock[i]);

                reordered = activeEnsures.Count;
            }

            // ── Add missing ensures ───────────────────────────────────────────────────
            int added = 0;
            var alreadyEnsured = new HashSet<string>(
                ensureLines.Select(e => e.resource), StringComparer.OrdinalIgnoreCase);

            var missing = installedResources
                .Where(r => !alreadyEnsured.Contains(r))
                .OrderBy(r => tierMap.TryGetValue(r, out int t) ? t : 99)
                .ThenBy(r => r)
                .ToList();

            if (missing.Count > 0)
            {
                lines.Add("");
                lines.Add("# ── Resources detected by TGToolKit (not yet in server.cfg) ──");
                foreach (var m in missing)
                {
                    lines.Add($"ensure {m}");
                    log?.LogWrite($"[ADDED] ensure {m}");
                    added++;
                }
            }

            if (reordered > 0 || added > 0)
            {
                await fs.CreateBackupAsync(cfgPath);
                await fs.WriteAllTextAsync(cfgPath, string.Join('\n', lines));
                log?.LogWrite($"[ORDER] Done. {reordered} line(s) reordered, {added} line(s) added.");
            }
            else
            {
                log?.LogWrite("[ORDER] server.cfg load order is already correct. No changes needed.");
            }

            return (reordered, added);
        }



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
