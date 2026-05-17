using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ToolKitV.Models.Providers;

namespace ToolKitV.Models
{
    public static class GlobalTranspiler
    {
        public static string GlobalRecipesDirectory => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "GlobalRecipes");

        public static void SeedDefaultRecipes()
        {
            if (!Directory.Exists(GlobalRecipesDirectory))
                Directory.CreateDirectory(GlobalRecipesDirectory);

            string notifyPath = Path.Combine(GlobalRecipesDirectory, "global_qbnotify_to_oxlib.json");
            if (!File.Exists(notifyPath))
            {
                string json = @"
{
  ""RecipeId"": ""global_qbnotify_to_oxlib"",
  ""RequiredResource"": ""ANY"",
  ""TargetResource"": ""ANY"",
  ""Description"": ""Upgrades legacy QBCore notifications to modern ox_lib notifications."",
  ""Patches"": [
    {
      ""TargetFilePath"": ""*.lua"",
      ""SearchSnippet"": ""QBCore\\.Functions\\.Notify\\((.*?),\\s*['\""](.*?)['\""]\\)"",
      ""ReplaceWith"": ""lib.notify({ title = 'Notification', description = $1, type = '$2' })"",
      ""IsRegex"": true
    }
  ]
}";
                File.WriteAllText(notifyPath, json.Trim());
            }

            string drawTextPath = Path.Combine(GlobalRecipesDirectory, "global_drawtext_to_oxlib.json");
            if (!File.Exists(drawTextPath))
            {
                string json = @"
{
  ""RecipeId"": ""global_drawtext_to_oxlib"",
  ""RequiredResource"": ""ANY"",
  ""TargetResource"": ""ANY"",
  ""Description"": ""Replaces unoptimized QBCore DrawText with ox_lib TextUI."",
  ""Patches"": [
    {
      ""TargetFilePath"": ""*.lua"",
      ""SearchSnippet"": ""exports\\['qb-core'\\]:DrawText\\((.*?),\\s*['\""](.*?)['\""]\\)"",
      ""ReplaceWith"": ""lib.showTextUI($1, { icon = 'hand', position = 'right-center' })"",
      ""IsRegex"": true
    },
    {
      ""TargetFilePath"": ""*.lua"",
      ""SearchSnippet"": ""exports\\['qb-core'\\]:HideText\\(\\)"",
      ""ReplaceWith"": ""lib.hideTextUI()"",
      ""IsRegex"": false
    }
  ]
}";
                File.WriteAllText(drawTextPath, json.Trim());
            }
        }

        public static List<IntegrationRecipe> LoadGlobalRecipes()
        {
            SeedDefaultRecipes();
            var recipes = new List<IntegrationRecipe>();
            foreach (var file in Directory.GetFiles(GlobalRecipesDirectory, "*.json"))
            {
                try
                {
                    string json = File.ReadAllText(file);
                    var r = System.Text.Json.JsonSerializer.Deserialize<IntegrationRecipe>(json);
                    if (r != null) recipes.Add(r);
                }
                catch { }
            }
            return recipes;
        }

        public static async Task<int> RunGlobalPassAsync(
            IFileSystemProvider fs,
            string workspaceRoot,
            List<IntegrationRecipe> globalRecipes,
            string? webhookUrl,
            string? locale,
            string? currency,
            string? currencyIcon,
            LogWriter? log,
            IProgress<int>? progress = null)
        {
            int filesModified = 0;
            log?.LogWrite($"=== Starting Global Transpiler Engine ===");
            log?.LogWrite($"[SCAN] Hunting .lua files in {workspaceRoot}...");

            // 1. Grab every single Lua client/server file in the workspace
            var allLuaFiles = await fs.DiscoverFilesAsync(workspaceRoot, "*.lua");
            
            // Exclude stream, html, ui to maintain blistering speed
            var targetFiles = allLuaFiles.Where(f => 
            {
                string norm = f.Replace('\\', '/').ToLowerInvariant();
                return !norm.Contains("/stream/") && !norm.Contains("/html/") && !norm.Contains("/ui/") && !norm.Contains("/web/");
            }).ToList();

            log?.LogWrite($"[SCAN] Found {targetFiles.Count} target files after filtering.");

            for (int i = 0; i < targetFiles.Count; i++)
            {
                string file = targetFiles[i];
                string originalText = await fs.ReadAllTextAsync(file);
                string workingText = originalText;
                bool fileNeedsSave = false;

                // 2. Run every global Qbox/Ox recipe against the file
                foreach (var recipe in globalRecipes)
                {
                    foreach (var patch in recipe.Patches)
                    {
                        // BridgeEngine.ApplyPatch works natively with text refs
                        if (BridgeEngine.ApplyPatch(ref workingText, patch, null))
                        {
                            fileNeedsSave = true;
                            log?.LogWrite($"[TRANSPILE] {Path.GetFileName(file)} -> {recipe.RecipeId}");
                        }
                    }
                }

                // 3. Webhook Overrides
                if (!string.IsNullOrWhiteSpace(webhookUrl))
                {
                    // Regex to catch variants of: Config.Webhook = "https://...", Webhook = '...', webhook = ""
                    string webhookPattern = @"(?i)(\bwebhook\s*=\s*['""]).*?(['""])";
                    if (Regex.IsMatch(workingText, webhookPattern))
                    {
                        workingText = Regex.Replace(workingText, webhookPattern, $"$1{webhookUrl}$2");
                        fileNeedsSave = true;
                        log?.LogWrite($"[WEBHOOK] Updated webhook in {Path.GetFileName(file)}");
                    }
                }

                // 4. Locale Overrides
                if (!string.IsNullOrWhiteSpace(locale))
                {
                    string localePattern = @"(?i)(\blocale\s*=\s*['""]).*?(['""])";
                    if (Regex.IsMatch(workingText, localePattern))
                    {
                        workingText = Regex.Replace(workingText, localePattern, $"$1{locale}$2");
                        fileNeedsSave = true;
                        log?.LogWrite($"[LOCALE] Synced locale to {locale} in {Path.GetFileName(file)}");
                    }
                }

                // 5. Currency & Currency Icon Overrides
                if (!string.IsNullOrWhiteSpace(currency))
                {
                    string currencyPattern = @"(?i)(\bcurrency\s*=\s*['""]).*?(['""])";
                    if (Regex.IsMatch(workingText, currencyPattern))
                    {
                        workingText = Regex.Replace(workingText, currencyPattern, $"$1{currency}$2");
                        fileNeedsSave = true;
                        log?.LogWrite($"[CURRENCY] Synced currency to {currency} in {Path.GetFileName(file)}");
                    }
                }

                if (!string.IsNullOrWhiteSpace(currencyIcon))
                {
                    string iconPattern = @"(?i)(\bcurrencyIcon\s*=\s*['""]).*?(['""])";
                    // Also hunt for currency_icon or CurrencySymbol
                    string symbolPattern = @"(?i)(\b(?:currency[_\s]*symbol|currency[_\s]*icon)\s*=\s*['""]).*?(['""])";

                    if (Regex.IsMatch(workingText, iconPattern))
                    {
                        workingText = Regex.Replace(workingText, iconPattern, $"$1{currencyIcon}$2");
                        fileNeedsSave = true;
                        log?.LogWrite($"[CURRENCY] Synced currency icon to {currencyIcon} in {Path.GetFileName(file)}");
                    }
                    else if (Regex.IsMatch(workingText, symbolPattern))
                    {
                        workingText = Regex.Replace(workingText, symbolPattern, $"$1{currencyIcon}$2");
                        fileNeedsSave = true;
                        log?.LogWrite($"[CURRENCY] Synced currency symbol to {currencyIcon} in {Path.GetFileName(file)}");
                    }
                }

                // 6. Save only if changes were actually made
                if (fileNeedsSave && workingText != originalText)
                {
                    await fs.CreateBackupAsync(file); // Ensure atomic backup
                    await fs.WriteAllTextAsync(file, workingText);
                    filesModified++;
                }

                // Progress
                progress?.Report((i + 1) * 100 / targetFiles.Count);
            }

            log?.LogWrite($"=== Transpiler Finished. {filesModified} file(s) upgraded. ===");
            return filesModified;
        }
    }
}
