using System;
using System.IO;
using System.Collections.Generic;
using System.Threading.Tasks;
using ToolKitV.Models.Providers;

namespace ToolKitV.Models
{
    public class DeploymentConfig
    {
        public string MySqlConnectionString { get; set; } = string.Empty;
        public string TargetInventory { get; set; } = string.Empty;
        public string MasterLocale { get; set; } = string.Empty;
        public string DiscordWebhook { get; set; } = string.Empty;
        public List<IntegrationRecipe> GlobalRecipes { get; set; } = new();
        public List<IntegrationRecipe> SpecificRecipes { get; set; } = new();
    }

    public class SetupOrchestrator
    {
        private readonly AuditLogger _auditLog = new();

        public AuditLogger AuditLog => _auditLog;

        public async Task<bool> RunZeroToHeroDeploymentAsync(
            IFileSystemProvider fs, 
            string remoteWorkspace, 
            DeploymentConfig config, 
            LogWriter log)
        {
            string tempWorkspace = Path.Combine(Path.GetTempPath(), "TGToolKit_Master");
            string snapshotZip = "";

            try
            {
                // STEP 1: Download & Secure
                log.LogWrite("[PHASE 1] Downloading and securing live workspace...");

                // If targeting a local folder, we MUST apply the exclusion mask to prevent 50GB disk crashes.
                // If using SFTP, the WinSCP provider already applies its own network-level exclusion mask.
                if (fs is LocalFileSystemProvider)
                {
                    await CloneLocalWorkspaceFilteredAsync(remoteWorkspace, tempWorkspace, log);
                }
                else
                {
                    await fs.DownloadDirectoryAsync(remoteWorkspace, tempWorkspace);
                }
                
                // --- THE FAILSAFE ---
                log.LogWrite("[PHASE 1] Compressing workspace snapshot...");
                snapshotZip = await SnapshotEngine.CreateSafetySnapshotAsync(tempWorkspace, log);
                log.LogWrite($"[PHASE 1 SUCCESS] Failsafe snapshot secured at {Path.GetFileName(snapshotZip)}.");

                // STEP 2: The Static Diagnostic Engine (Fix broken syntax first)
                log.LogWrite("[PHASE 2] Running Static Code Diagnostics...");
                int fixedSyntaxCount = await DiagnosticEngine.RunStaticAnalysisAsync(tempWorkspace, _auditLog);
                log.LogWrite($"[PHASE 2 SUCCESS] Syntax check done. Corrected {fixedSyntaxCount} Lua file(s).");

                // STEP 3: Server Linter (Fix manifests)
                log.LogWrite("[PHASE 3] Running Manifest Auto-Fixer...");
                int manifestFixesCount = await LinterAutoFixer.FixManifestErrorsAsync(fs, tempWorkspace, log);
                log.LogWrite($"[PHASE 3 SUCCESS] Manifest fixes complete. Standardized {manifestFixesCount} file(s).");

                // STEP 4: The SQL Matrix (Build Database Tables)
                log.LogWrite("[PHASE 4] Executing SQL Matrix Database Injection...");
                int sqlTablesCount = await SqlInjector.InjectDatabaseTablesAsync(tempWorkspace, config.MySqlConnectionString, _auditLog);
                log.LogWrite($"[PHASE 4 SUCCESS] Database check complete. Injected {sqlTablesCount} SQL table(s).");

                // STEP 5: Asset Harvesting (Images to inventory)
                log.LogWrite("[PHASE 5] Harvesting items and injecting into Master Inventory...");
                var harvestedItems = ItemHarvester.HarvestFromDirectory(tempWorkspace);
                log.LogWrite($"[PHASE 5] Harvested {harvestedItems.Count} custom item(s) from resources.");
                if (harvestedItems.Count > 0)
                {
                    await InventoryInjector.InjectItemsAsync(fs, tempWorkspace, config.TargetInventory, harvestedItems, _auditLog);
                    log.LogWrite($"[PHASE 5 SUCCESS] Successfully injected items into '{config.TargetInventory}'.");
                }
                else
                {
                    log.LogWrite("[PHASE 5 SKIP] No new custom items found to harvest.");
                }

                // STEP 6: Global Transpiler (Qbox/ox_lib standardizations)
                log.LogWrite("[PHASE 6] Transpiling legacy code to modern Qbox/Ox standards...");
                int transpiledFiles = await GlobalTranspiler.RunGlobalPassAsync(
                    fs,
                    tempWorkspace,
                    config.GlobalRecipes,
                    config.DiscordWebhook,
                    config.MasterLocale,
                    null,
                    null,
                    log
                );
                log.LogWrite($"[PHASE 6 SUCCESS] Transpiled {transpiledFiles} file(s) to modern standard exports.");

                // STEP 7: Integration Bridge (JPR/JG/Lation exact patches)
                log.LogWrite("[PHASE 7] Applying target integration bridge recipes...");
                await BridgeEngine.ApplyRecipesAsync(fs, tempWorkspace, config.SpecificRecipes, log);
                log.LogWrite("[PHASE 7 SUCCESS] Integration recipes successfully applied.");

                // STEP 8: Voice, Webhooks, and Locales
                log.LogWrite("[PHASE 8] Syncing Master Locales and Webhooks...");
                int localesSynced = await LocalizationEngine.SyncAllLocalesAsync(tempWorkspace, config.MasterLocale, _auditLog);
                int webhooksInjected = await WebhookEngine.InjectMasterWebhookAsync(tempWorkspace, config.DiscordWebhook, _auditLog);
                log.LogWrite($"[PHASE 8 SUCCESS] Synced locales in {localesSynced} file(s). Injected webhooks in {webhooksInjected} file(s).");

                // STEP 9: The Upload Delta
                log.LogWrite("[PHASE 9] Pushing optimized, secure ecosystem to live server...");
                await UploadDirectoryDeltaAsync(fs, tempWorkspace, remoteWorkspace, log);
                log.LogWrite("[PHASE 9 SUCCESS] Codebase push complete.");

                log.LogWrite("=== DEPLOYMENT COMPLETE. SERVER IS READY FOR LAUNCH ===");

                // Drop Deployment Report on Desktop as requested
                try
                {
                    string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                    string reportPath = Path.Combine(desktopPath, "deployment_report.txt");
                    string reportContent = _auditLog.GenerateReport();
                    await File.WriteAllTextAsync(reportPath, reportContent);
                    log.LogWrite($"[SUCCESS] Deployment report dropped on Desktop: {reportPath}");
                }
                catch (Exception ex)
                {
                    log.LogWrite($"[WARNING] Failed to drop report on Desktop: {ex.Message}");
                }

                return true;
            }
            catch (Exception ex)
            {
                log.LogWrite($"[CRITICAL FAILURE] {ex.Message}");
                log.LogWrite("[ROLLBACK] Initiating safety recovery...");
                if (!string.IsNullOrEmpty(snapshotZip) && File.Exists(snapshotZip))
                {
                    try
                    {
                        await SnapshotEngine.RestoreSnapshotAsync(snapshotZip, tempWorkspace, log);
                        log.LogWrite("[ROLLBACK SUCCESS] Workspace restored to pre-deployment state.");
                    }
                    catch (Exception rex)
                    {
                        log.LogWrite($"[CRITICAL ROLLBACK FAILURE] Failsafe recovery failed: {rex.Message}");
                    }
                }
                return false;
            }
        }

        private async Task UploadDirectoryDeltaAsync(
            IFileSystemProvider fs, 
            string localPath, 
            string remotePath,
            LogWriter log)
        {
            var localFiles = Directory.GetFiles(localPath, "*", SearchOption.AllDirectories);
            int count = 0;

            foreach (var localFile in localFiles)
            {
                string relPath = Path.GetRelativePath(localPath, localFile);
                string remoteFile = Path.Combine(remotePath, relPath).Replace('\\', '/');

                if (localFile.EndsWith(".zip") || localFile.EndsWith(".tg_backup") || localFile.Contains(".tg_backup")) continue;

                await fs.UploadFileAsync(localFile, remoteFile);
                count++;
            }
            log.LogWrite($"[PHASE 9] Transferred {count} files to the server.");
        }

        private async Task CloneLocalWorkspaceFilteredAsync(string sourceRoot, string targetRoot, LogWriter log)
        {
            // Allowed: Code, configs, databases, and UI assets (required for Asset Harvester)
            var allowedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) 
            { 
                ".lua", ".cfg", ".meta", ".json", ".sql", 
                ".html", ".js", ".css", ".png", ".jpg", ".svg" 
            };

            // Forbidden: Massive 3D models, audio, and git history
            var forbiddenFolders = new[] 
            { 
                "\\stream\\", "/stream/", "\\node_modules\\", "/node_modules/", "\\.git\\", "/.git/" 
            };

            await Task.Run(() =>
            {
                var allFiles = Directory.EnumerateFiles(sourceRoot, "*.*", SearchOption.AllDirectories);

                foreach (var file in allFiles)
                {
                    // 1. Instantly skip if it's inside a stream folder or node_modules or git
                    if (forbiddenFolders.Any(folder => file.IndexOf(folder, StringComparison.OrdinalIgnoreCase) >= 0))
                        continue;

                    // 2. Instantly skip if it's a heavy asset (.yft, .ytd, .awc, etc.)
                    string ext = Path.GetExtension(file);
                    if (!allowedExtensions.Contains(ext))
                        continue;

                    // 3. Mirror the directory structure in the temp folder and copy
                    string relativePath = Path.GetRelativePath(sourceRoot, file);
                    string targetFilePath = Path.Combine(targetRoot, relativePath);

                    string? dir = Path.GetDirectoryName(targetFilePath);
                    if (dir != null)
                    {
                        Directory.CreateDirectory(dir);
                    }
                    File.Copy(file, targetFilePath, overwrite: true);
                }
            });
        }
    }
}
