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
        /// Scans <paramref name="rootPath"/> for all <c>.tg_backup</c> files created by the
        /// Auto-Wirer, Conflict Resolver, or server.cfg fixer. Restores each original file
        /// from its backup then deletes the <c>.tg_backup</c>. Works over local disk or SFTP.
        /// </summary>
        public static async Task<int> RestoreBackupsAsync(
            IFileSystemProvider fs,
            string              rootPath,
            LogWriter?          log)
        {
            int restored = 0;
            log?.LogWrite("=== Emergency Rollback — Restoring .tg_backup files ===");

            var backups = await fs.DiscoverFilesAsync(rootPath, "*.tg_backup");

            foreach (var backupPath in backups)
            {
                // Original path = backup minus the ".tg_backup" suffix
                string originalPath = backupPath[..^".tg_backup".Length];
                try
                {
                    string content = await fs.ReadAllTextAsync(backupPath);
                    await fs.WriteAllTextAsync(originalPath, content);
                    await fs.DeleteFileAsync(backupPath);
                    restored++;
                    log?.LogWrite($"[RESTORED] {Path.GetFileName(originalPath)}");
                }
                catch (Exception ex)
                {
                    log?.LogWrite($"[ERROR] Could not restore {Path.GetFileName(originalPath)}: {ex.Message}");
                }
            }

            log?.LogWrite($"=== Rollback complete. {restored} file(s) restored. ===");
            return restored;
        }



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

            // ── 1. OMNI-ECOSYSTEM DETECTION ──────────────────────────────────────────

            // ── Core ─────────────────────────────────────────────────────────────────
            string activeFramework = installedResources.Contains("qbx_core")    ? "qbox"      :
                                     installedResources.Contains("qb-core")     ? "qb"        :
                                     installedResources.Contains("es_extended") ? "esx"       : "standalone";

            string activeInventory = installedResources.Contains("ox_inventory")  ? "ox"      :
                                     installedResources.Contains("qs-inventory")  ? "quasar"  :
                                     installedResources.Contains("ps-inventory")  ? "ps"      :
                                     installedResources.Contains("jpr-inventory") ? "jpr"     : "qb";

            string activeTarget    = installedResources.Contains("ox_target")  ? "ox_target" :
                                     installedResources.Contains("qb-target")  ? "qb-target" : "none";

            string activeUI        = installedResources.Contains("lation_ui")     ? "lation"  :
                                     installedResources.Contains("okokNotify")    ? "okok"    :
                                     installedResources.Contains("mythic_notify") ? "mythic"  :
                                     installedResources.Contains("ox_lib")        ? "ox"      : "qb";

            // ── Communications ───────────────────────────────────────────────────────
            string activePhone     = installedResources.Contains("jpr-phonesystem") ? "jpr-phonesystem" :
                                     installedResources.Contains("lb-phone")        ? "lb-phone"        :
                                     installedResources.Contains("qs-smartphone")   ? "qs-smartphone"   :
                                     installedResources.Contains("renewed-phone")   ? "renewed-phone"   : "qb-phone";

            string activeRadio     = installedResources.Contains("pma-voice") ? "pma" : "mumble";

            // ── Justice & Medical ─────────────────────────────────────────────────────
            string activeDispatch  = installedResources.Contains("ps-dispatch")  ? "ps-dispatch"  :
                                     installedResources.Contains("qs-dispatch")  ? "qs-dispatch"  :
                                     installedResources.Contains("cd_dispatch")  ? "cd-dispatch"  : "qb-core";

            string activeMDT       = installedResources.Contains("xd_mdt")         ? "xd"      :
                                     installedResources.Contains("jpr-mdtsystem")  ? "jpr"     :
                                     installedResources.Contains("ps-mdt")         ? "ps"      :
                                     installedResources.Contains("redutzu-mdt")    ? "redutzu" : "none";

            string activeBilling   = installedResources.Contains("okokBilling")     ? "okok"    :
                                     installedResources.Contains("xd_billing")      ? "xd"      :
                                     installedResources.Contains("renewed-banking") ? "renewed" : "qb-phone";

            // ── Vehicles & World ──────────────────────────────────────────────────────
            string activeFuel      = installedResources.Contains("ox_fuel")    ? "ox"     :
                                     installedResources.Contains("ps-fuel")    ? "ps"     :
                                     installedResources.Contains("LegacyFuel") ? "legacy" : "none";

            string activeKeys      = installedResources.Contains("jpr-keys")       ? "jpr"            :
                                     installedResources.Contains("qs-vehiclekeys") ? "quasar"         :
                                     installedResources.Contains("wasabi_carlock") ? "wasabi"         : "qb-vehiclekeys";

            string activeWeather   = installedResources.Contains("cd_easytime")    ? "cd_easytime"    :
                                     installedResources.Contains("qb-weathersync") ? "qb-weathersync" :
                                     installedResources.Contains("vSync")          ? "vSync"          : "none";

            // ── Utilities ─────────────────────────────────────────────────────────────
            string activeMinigame  = installedResources.Contains("ps-ui")        ? "ps-ui" :
                                     installedResources.Contains("xd_minigames") ? "xd"    : "none";

            log?.LogWrite($"[OMNI-ECOSYSTEM] Fw:{activeFramework} | Inv:{activeInventory} | Tgt:{activeTarget} | UI:{activeUI} | Ph:{activePhone}");
            log?.LogWrite($"[OMNI-ECOSYSTEM] Disp:{activeDispatch} | MDT:{activeMDT} | Bill:{activeBilling} | Fuel:{activeFuel} | Keys:{activeKeys} | Wx:{activeWeather}");

            // ── 2. SERVER.CFG MASTER ENGINE (SFTP-Safe) ──────────────────────────────
            // server.cfg lives ONE directory ABOVE the resources folder.
            // e.g.  /home/container/resources  →  /home/container/server.cfg
            // Using pure string ops (no Path.Combine / System.IO) so it works over SFTP.
            try
            {
                string resourcesNorm = serverRootPath.TrimEnd('/', '\\').Replace('\\', '/');
                string serverRoot    = resourcesNorm.Contains('/')
                    ? resourcesNorm.Substring(0, resourcesNorm.LastIndexOf('/'))
                    : "/";
                if (string.IsNullOrWhiteSpace(serverRoot)) serverRoot = "/";

                string serverCfgPath = serverRoot + "/server.cfg";

                // Probe: DiscoverFilesAsync("server.cfg") in the parent dir to confirm it exists
                var cfgProbe = await fs.DiscoverFilesAsync(serverRoot, "server.cfg");

                if (cfgProbe.Count > 0)
                {
                    string cfgText    = await fs.ReadAllTextAsync(serverCfgPath);
                    bool   cfgChanged = false;

                    log?.LogWrite($"[CFG] Found server.cfg at {serverCfgPath}. Analysing...");

                    // ── Fix 1: Game Build Enforcement ────────────────────────────────────
                    // Modern DLC scripts fatal-crash if sv_enforceGameBuild is missing or too low.
                    if (!Regex.IsMatch(cfgText, @"(?i)sv_enforceGameBuild"))
                    {
                        log?.LogWrite("[FIX] Injecting sv_enforceGameBuild 3258 (Bottom Dollar Bounties) into server.cfg.");
                        cfgText   = "set sv_enforceGameBuild 3258\n" + cfgText;
                        cfgChanged = true;
                    }

                    // ── Fix 2: OneSync Enforcement ───────────────────────────────────────
                    // Required by ox_target routing buckets and modern resource systems.
                    if (!Regex.IsMatch(cfgText, @"(?i)set\s+onesync\s+(on|legacy|1)"))
                    {
                        log?.LogWrite("[FIX] Injecting 'set onesync on' into server.cfg.");
                        cfgText   = "set onesync on\n" + cfgText;
                        cfgChanged = true;
                    }

                    // ── Fix 3: pma-voice convars ─────────────────────────────────────────
                    if (installedResources.Contains("pma-voice") &&
                        !Regex.IsMatch(cfgText, @"(?i)voice_useNativeAudio"))
                    {
                        log?.LogWrite("[FIX] Injecting pma-voice optimal convars into server.cfg.");
                        string voiceBlock =
                            "\n# TGToolKit: Auto-Wired Voice Settings\n" +
                            "setr voice_useNativeAudio true\n"            +
                            "setr voice_use3dAudio true\n"                +
                            "setr voice_defaultCycle \"GRAVE\"\n";
                        cfgText   = voiceBlock + cfgText;
                        cfgChanged = true;

                        // Mute conflicting mumble-voip ensures
                        cfgText = Regex.Replace(cfgText,
                            @"(?mi)^(\s*(?:ensure|start)\s+mumble-voip\s*)$",
                            "# [TGToolKit] Auto-disabled: conflicts with pma-voice\n# $1");
                    }

                    // ── Fix 4: oxmysql connection string ────────────────────────────────
                    if (installedResources.Contains("oxmysql") &&
                        !Regex.IsMatch(cfgText, @"(?i)mysql_connection_string"))
                    {
                        log?.LogWrite("[FIX] Injecting missing oxmysql connection string template.");
                        cfgText   = "# [TGToolKit] Configure your DB credentials below:\n" +
                                    "set mysql_connection_string \"mysql://root:password@localhost/fivem?charset=utf8mb4\"\n" +
                                    cfgText;
                        cfgChanged = true;
                    }

                    // ── Fix 5: God-Tier Core Load Order Re-Wire ──────────────────────────
                    // Finds core lib ensures anywhere in the file, comments out their old
                    // position, and forces them to the top of the resource boot block.
                    string[] coreLibs = { "oxmysql", "ox_lib", "lation_core", "wasabi_bridge", "jpr-libs", "xd_lib" };
                    string   topBlock = "\n# TGToolKit: Core Load Order Enforcement\n";
                    bool     reordered = false;

                    foreach (string lib in coreLibs)
                    {
                        if (!installedResources.Contains(lib)) continue;

                        string libPattern = $@"(?mi)^((?:ensure|start)\s+{Regex.Escape(lib)}\s*)$";
                        if (!Regex.IsMatch(cfgText, libPattern)) continue;

                        // Comment out old position, queue for top block
                        cfgText    = Regex.Replace(cfgText, libPattern,
                            $"# [TGToolKit] Auto-moved to top: $1");
                        topBlock  += $"ensure {lib}\n";
                        reordered  = true;
                        log?.LogWrite($"[ORDER] Hoisted '{lib}' to top of boot order.");
                    }

                    if (reordered)
                    {
                        // Anchor: insert the block right after the last injected convar header
                        // (sv_enforceGameBuild or onesync), falling back to file head.
                        string anchored = Regex.Replace(cfgText,
                            @"(set onesync on\r?\n|set sv_enforceGameBuild \d+\r?\n)",
                            "$1" + topBlock,
                            RegexOptions.None,
                            TimeSpan.FromSeconds(2));

                        cfgText    = cfgText.Contains(topBlock) ? cfgText : anchored.Contains(topBlock) ? anchored : topBlock + cfgText;
                        cfgChanged  = true;
                        log?.LogWrite("[ORDER] Core dependency load order re-wired successfully.");
                    }

                    // ── Commit ───────────────────────────────────────────────────────────
                    if (cfgChanged)
                    {
                        await fs.CreateBackupAsync(serverCfgPath);
                        await fs.WriteAllTextAsync(serverCfgPath, cfgText);
                        fixesApplied++;
                        log?.LogWrite($"[SUCCESS] Advanced server.cfg auto-wiring complete at {serverCfgPath}");
                    }
                }
                else
                {
                    log?.LogWrite($"[WARNING] server.cfg not found under '{serverRoot}'. " +
                                  "Skipping config injections — Lua files will still be processed.");
                }
            }
            catch (Exception cfgEx)
            {
                log?.LogWrite($"[ERROR] server.cfg engine encountered a fatal error: {cfgEx.Message}");
            }

            // ── 3. Universal fuzzy config re-routing ─────────────────────────────────
            var allManifests = await fs.DiscoverFilesAsync(serverRootPath, "fxmanifest.lua");

            foreach (var manifestPath in allManifests)
            {
                // Normalise to forward-slash for safe string ops on both local and remote paths
                string normalised   = manifestPath.Replace('\\', '/');
                string resourceDir  = normalised.Substring(0, normalised.LastIndexOf('/'));
                string resourceName = resourceDir.Substring(resourceDir.LastIndexOf('/') + 1);

                // ── 2b. Inject missing fx_version / game into EXISTING fxmanifest.lua ───
                // This addresses the "128 issues" where scripts have fxmanifest.lua files
                // but are missing the mandatory FiveM header declarations.
                try
                {
                    string manifestText    = await fs.ReadAllTextAsync(normalised);
                    bool   manifestChanged = false;

                    if (!Regex.IsMatch(manifestText, @"fx_version\s+['""]"))
                    {
                        manifestText   = "fx_version 'cerulean'\n" + manifestText;
                        manifestChanged = true;
                        log?.LogWrite($"[FIX] Injected fx_version into {resourceName}/fxmanifest.lua");
                    }

                    if (!Regex.IsMatch(manifestText, @"game\s+['""]"))
                    {
                        manifestText = Regex.Replace(manifestText,
                            @"(fx_version\s+['""][^'""]*['""][\r\n]*)",
                            "$1game 'gta5'\n");
                        manifestChanged = true;
                        log?.LogWrite($"[FIX] Injected game declaration into {resourceName}/fxmanifest.lua");
                    }

                    if (manifestChanged)
                    {
                        await fs.CreateBackupAsync(normalised);
                        await fs.WriteAllTextAsync(normalised, manifestText);
                        fixesApplied++;
                    }
                }
                catch (Exception ex)
                {
                    log?.LogWrite($"[ERROR] Headers fix failed for {resourceName}/fxmanifest.lua: {ex.Message}");
                }

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

                        // ── OMNI-INJECTOR: route every ecosystem variable ──────────────────
                        bool modified = false;

                        // Core Bridges
                        modified |= TryInjectConfig(ref luaText, "Framework",    activeFramework);
                        modified |= TryInjectConfig(ref luaText, "Core",         activeFramework);
                        modified |= TryInjectConfig(ref luaText, "Inventory",    activeInventory);
                        modified |= TryInjectConfig(ref luaText, "Target",       activeTarget);
                        modified |= TryInjectConfig(ref luaText, "TargetSystem", activeTarget);

                        // UI & Notifications
                        modified |= TryInjectConfig(ref luaText, "UI",           activeUI);
                        modified |= TryInjectConfig(ref luaText, "Notify",       activeUI);
                        modified |= TryInjectConfig(ref luaText, "Notification", activeUI);
                        modified |= TryInjectConfig(ref luaText, "ProgressBar",  activeUI);
                        modified |= TryInjectConfig(ref luaText, "Minigame",     activeMinigame);
                        modified |= TryInjectConfig(ref luaText, "Skillbar",     activeMinigame);

                        // Communications
                        modified |= TryInjectConfig(ref luaText, "Phone",        activePhone);
                        modified |= TryInjectConfig(ref luaText, "Radio",        activeRadio);
                        modified |= TryInjectConfig(ref luaText, "Voice",        activeRadio);

                        // Justice & Economy
                        modified |= TryInjectConfig(ref luaText, "Dispatch",       activeDispatch);
                        modified |= TryInjectConfig(ref luaText, "DispatchSystem", activeDispatch);
                        modified |= TryInjectConfig(ref luaText, "MDT",           activeMDT);
                        modified |= TryInjectConfig(ref luaText, "Billing",       activeBilling);
                        modified |= TryInjectConfig(ref luaText, "Invoices",      activeBilling);
                        modified |= TryInjectConfig(ref luaText, "Banking",       activeBilling);

                        // Vehicles
                        modified |= TryInjectConfig(ref luaText, "Fuel",        activeFuel);
                        modified |= TryInjectConfig(ref luaText, "FuelSystem",   activeFuel);
                        modified |= TryInjectConfig(ref luaText, "Keys",         activeKeys);
                        modified |= TryInjectConfig(ref luaText, "VehicleKeys",  activeKeys);

                        // Weather
                        modified |= TryInjectConfig(ref luaText, "Weather",     activeWeather);
                        modified |= TryInjectConfig(ref luaText, "WeatherSync",  activeWeather);

                        // Databases
                        if (installedResources.Contains("oxmysql"))
                        {
                            modified |= TryInjectConfig(ref luaText, "Database", "oxmysql");
                            modified |= TryInjectConfig(ref luaText, "Mysql",    "oxmysql");
                            modified |= TryInjectConfig(ref luaText, "SQL",      "oxmysql");
                        }

                        // Qbox native code-path unlock
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
                    catch (Exception ex)
                    {
                        log?.LogWrite($"[ERROR] Config wire failed for {resourceName}: {ex.Message}");
                    }
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
                        string norm = manifestPath.Replace('\\', '/');
                        string resourceDir = norm.Substring(0, norm.LastIndexOf('/'));
                        
                        var luaFiles = await fs.DiscoverFilesAsync(resourceDir, "*.lua");
                        bool usesOxLib = false;
                        foreach (var f in luaFiles)
                        {
                            if (f.Replace('\\', '/') == norm) continue; // skip manifest
                            string luaContent = await fs.ReadAllTextAsync(f);
                            if (luaContent.Contains("lib.") || luaContent.Contains("ox_lib"))
                            {
                                usesOxLib = true;
                                break;
                            }
                        }

                        if (usesOxLib && !text.Contains("@ox_lib"))
                        {
                            string rn = Path.GetFileName(resourceDir);
                            log?.LogWrite($"[FIX] Injecting @ox_lib/init.lua into {rn}/fxmanifest.lua");
                            await fs.CreateBackupAsync(manifestPath);
                            text += "\n-- [TGToolKit] Injected missing ox_lib dependency\nshared_script '@ox_lib/init.lua'\n";
                            await fs.WriteAllTextAsync(manifestPath, text);
                            fixesApplied++;
                        }
                    }
                    catch (Exception ex)
                    {
                        string rn2 = manifestPath.Replace('\\', '/').Split('/')[^2];
                        log?.LogWrite($"[ERROR] ox_lib injection failed for {rn2}: {ex.Message}");
                    }
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

        /// <summary>
        /// Dedicated two-sweep Manifest Auto-Fixer. Completely isolated from the
        /// Auto-Wirer so SFTP path discovery never gets confused between the two concerns.
        ///
        /// Sweep 1 — Converts every <c>__resource.lua</c> found under
        ///            <paramref name="serverRootPath"/> to a properly-headed
        ///            <c>fxmanifest.lua</c> and deletes the old file.
        ///
        /// Sweep 2 — Scans every <c>fxmanifest.lua</c> and injects a canonical
        ///            <c>fx_version</c> / <c>game</c> header block when missing.
        ///
        /// All modified files receive a <c>.tg_backup</c> before changes are applied.
        /// Works over local disk OR SFTP (provider-abstracted).
        /// </summary>
        public static async Task<int> FixManifestErrorsAsync(
            IFileSystemProvider fs,
            string              serverRootPath,
            LogWriter?          log)
        {
            int fixesApplied = 0;
            log?.LogWrite("=== Starting Dedicated Manifest Auto-Fixer ===");

            // Create a local staging area that mirrors the remote file structure
            string stagingDir = Path.Combine(Path.GetTempPath(), "TGToolKit_Staging_Manifests");
            if (Directory.Exists(stagingDir)) Directory.Delete(stagingDir, true);
            Directory.CreateDirectory(stagingDir);

            // ─── SWEEP 1: Convert Legacy __resource.lua files ────────────────────────
            var legacyManifests = await fs.DiscoverFilesAsync(serverRootPath, "__resource.lua");
            log?.LogWrite($"[SWEEP 1] Found {legacyManifests.Count} __resource.lua file(s) to convert.");

            foreach (var oldManifest in legacyManifests)
            {
                try
                {
                    // Safely extract directory using forward slashes (Linux / SFTP safe)
                    string norm    = oldManifest.Replace('\\', '/');
                    string dirPath = norm.Substring(0, norm.LastIndexOf('/'));
                    string newManifestPath = dirPath + "/fxmanifest.lua";

                    string text = await fs.ReadAllTextAsync(oldManifest);

                    // Inject modern headers if missing
                    if (!text.Contains("fx_version"))
                        text = "fx_version 'cerulean'\ngame 'gta5'\n\n" + text;

                    // Strip legacy manifest version declaration
                    text = Regex.Replace(text, @"resource_manifest_version\s+'[^']*'\s*\n?", string.Empty);

                    // Modernise block syntax (normalise whitespace before braces)
                    text = Regex.Replace(text, @"server_scripts\s*\{",  "server_scripts {");
                    text = Regex.Replace(text, @"client_scripts\s*\{",  "client_scripts {");
                    text = Regex.Replace(text, @"shared_scripts\s*\{",  "shared_scripts {");
                    text = Regex.Replace(text, @"files\s*\{",           "files {");

                    // Stage the new manifest locally
                    string relativePath = Path.GetRelativePath(serverRootPath, newManifestPath);
                    string localStagedFile = Path.Combine(stagingDir, relativePath);
                    string? localDir = Path.GetDirectoryName(localStagedFile);
                    if (localDir != null) Directory.CreateDirectory(localDir);
                    File.WriteAllText(localStagedFile, text);

                    // Delete the old legacy manifest on server
                    await fs.DeleteFileAsync(oldManifest);

                    fixesApplied++;
                    log?.LogWrite($"[STAGED] Converted {oldManifest} → fxmanifest.lua (staged)");
                }
                catch (Exception ex)
                {
                    log?.LogWrite($"[ERROR] Failed to convert {oldManifest}: {ex.Message}");
                }
            }

            // ─── SWEEP 2: Fix existing fxmanifest.lua — inject missing headers ────────
            var allManifests = await fs.DiscoverFilesAsync(serverRootPath, "fxmanifest.lua");
            log?.LogWrite($"[SWEEP 2] Found {allManifests.Count} fxmanifest.lua file(s) to inspect.");

            foreach (var manifest in allManifests)
            {
                try
                {
                    string text     = await fs.ReadAllTextAsync(manifest);
                    bool   modified = false;

                    // Missing fx_version?
                    if (!Regex.IsMatch(text, @"fx_version\s+['""]"))
                    {
                        text     = "fx_version 'cerulean'\n" + text;
                        modified = true;
                    }

                    // Missing game declaration?
                    if (!Regex.IsMatch(text, @"game\s+['""]"))
                    {
                        // Insert right below fx_version when possible
                        string patched = Regex.Replace(text,
                            @"(fx_version\s+['""][^'""]*['""][\r\n]*)",
                            "$1game 'gta5'\n");

                        // Fallback: prepend if the above substitution changed nothing
                        text     = patched.Contains("game 'gta5'") ? patched : "game 'gta5'\n" + text;
                        modified = true;
                    }

                    // Missing ox_lib dependency?
                    string norm = manifest.Replace('\\', '/');
                    string resourceDir = norm.Substring(0, norm.LastIndexOf('/'));
                    
                    var luaFiles = await fs.DiscoverFilesAsync(resourceDir, "*.lua");
                    bool usesOxLib = false;
                    foreach (var f in luaFiles)
                    {
                        if (f.Replace('\\', '/') == norm) continue; // skip manifest
                        string luaContent = await fs.ReadAllTextAsync(f);
                        // Check for ox_lib usage (e.g. lib.print, lib.notify, exports.ox_lib)
                        if (luaContent.Contains("lib.") || luaContent.Contains("ox_lib"))
                        {
                            usesOxLib = true;
                            break;
                        }
                    }

                    if (usesOxLib && !text.Contains("@ox_lib"))
                    {
                        text += "\n-- [TGToolKit] Injected missing ox_lib dependency\nshared_script '@ox_lib/init.lua'\n";
                        modified = true;
                    }

                    if (modified)
                    {
                        await fs.CreateBackupAsync(manifest);
                        
                        // Stage the modified manifest locally
                        string relativePath = Path.GetRelativePath(serverRootPath, manifest);
                        string localStagedFile = Path.Combine(stagingDir, relativePath);
                        string? localDir = Path.GetDirectoryName(localStagedFile);
                        if (localDir != null) Directory.CreateDirectory(localDir);
                        File.WriteAllText(localStagedFile, text);

                        fixesApplied++;
                        log?.LogWrite($"[STAGED] Injected missing headers into {manifest} (staged)");
                    }
                }
                catch (Exception ex)
                {
                    log?.LogWrite($"[ERROR] Failed to fix headers in {manifest}: {ex.Message}");
                }
            }

            // Blast all patched manifests back to the server in exactly ONE transaction
            if (fixesApplied > 0)
            {
                log?.LogWrite($"[MANIFEST] Pipelining {fixesApplied} modified/converted manifests to the server...");
                await fs.UploadDirectoryBulkAsync(stagingDir, serverRootPath, log);
            }

            try { Directory.Delete(stagingDir, true); } catch { }

            log?.LogWrite($"=== Manifest Auto-Fixer Finished. {fixesApplied} file(s) fixed. ===");
            return fixesApplied;
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



        /// <summary>
        /// Intelligently replaces configuration string variables using robust Regex.
        /// Handles all common FiveM config syntaxes:
        ///   Config.Inventory = 'qb'      — prefixed dot-notation
        ///   cfg.Phone="lb-phone"          — prefixed, double-quoted
        ///   Shared = { Inventory = 'ox' } — bare table-key syntax
        /// Skips values already set correctly, and never overwrites a 'custom' sentinel.
        /// </summary>
        private static bool TryInjectConfig(ref string text, string variableName, string newValue)
        {
            // Matches all common FiveM config syntaxes:
            //   Config.Inventory = 'qb'     — prefixed dot-notation, single-quoted
            //   cfg.Phone="lb-phone"        — prefixed, double-quoted
            //   Inventory = 'ox'            — bare table-key (inside a Shared table)
            //
            // Uses a regular (non-verbatim) string so we can safely include both
            // ' and " in the character class without verbatim-string quote escaping issues.
            //
            // Group 1 — prefix + variable + equals
            // Group 2 — opening quote
            // Group 3 — existing value
            // Group 4 — closing quote
            string pattern = "(?i)((?:(?:Config|cfg|shared|Cfg)\\." + variableName
                           + "|\\b" + variableName + "\\b)\\s*=\\s*)(['\"])(.*?)(['\"])";

            if (!Regex.IsMatch(text, pattern)) return false;

            var    match    = Regex.Match(text, pattern);
            string oldValue = match.Groups[3].Value;

            // Skip if already correct, or if the owner deliberately set 'custom' as a sentinel
            if (oldValue.Equals(newValue,  StringComparison.OrdinalIgnoreCase)) return false;
            if (oldValue.Equals("custom",  StringComparison.OrdinalIgnoreCase)) return false;

            // Swap only the value, preserving the surrounding quote type and whitespace exactly
            text = Regex.Replace(text, pattern, "$1$2" + newValue + "$4");
            return true;
        }
    }
}
