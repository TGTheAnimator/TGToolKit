using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace ToolKitV.Models
{
    public static class BridgeEngine
    {
        /// <summary>
        /// Converts raw Lua snippet into a bulletproof Regex pattern that ignores spaces, tabs, and newlines.
        /// </summary>
        public static string BuildBulletproofRegex(string codeSnippet)
        {
            if (string.IsNullOrWhiteSpace(codeSnippet)) return string.Empty;

            // 1. Split the code into raw tokens by any whitespace (spaces, tabs, newlines)
            var tokens = Regex.Split(codeSnippet.Trim(), @"\s+");

            // 2. Escape regex control characters (like [ ] { } . * ?) so they are treated as literal text
            var escapedTokens = tokens.Select(Regex.Escape);

            // 3. Rejoin the tokens with '\s*' allowing zero-or-more whitespace characters between everything.
            // Example input:  "local x = 5"
            // Example output: "local\s*x\s*=\s*5" -> This perfectly matches "local x=5", "local   x  =  5", etc.
            return string.Join(@"\s*", escapedTokens);
        }

        public static bool ApplyPatch(ref string fileText, FilePatch patch, LogWriter? log)
        {
            try
            {
                if (patch.IsRegex)
                {
                    // Advanced Mode: The JSON recipe provided a raw Regex pattern
                    if (Regex.IsMatch(fileText, patch.SearchSnippet))
                    {
                        fileText = Regex.Replace(fileText, patch.SearchSnippet, patch.ReplaceWith);
                        return true;
                    }
                }
                else
                {
                    // Standard Mode: Use the whitespace-agnostic builder
                    string flexPattern = BuildBulletproofRegex(patch.SearchSnippet);
                    
                    if (Regex.IsMatch(fileText, flexPattern))
                    {
                        fileText = Regex.Replace(fileText, flexPattern, patch.ReplaceWith);
                        return true;
                    }
                }
                
                return false; // Snippet not found in the file
            }
            catch (RegexMatchTimeoutException)
            {
                log.LogWrite($"[CRITICAL] Regex timeout while attempting to patch {patch.TargetFilePath}. Pattern may be too broad.");
                return false;
            }
            catch (Exception ex)
            {
                log?.LogWrite($"[CRITICAL] Regex error while attempting to patch {patch.TargetFilePath}: {ex.Message}");
                return false;
            }
        }
        public static async System.Threading.Tasks.Task ApplyRecipesAsync(
            Providers.IFileSystemProvider provider, 
            string rootPath, 
            System.Collections.Generic.List<IntegrationRecipe> recipes, 
            LogWriter log)
        {
            var audit = new AuditLogger();
            rootPath = rootPath.TrimEnd('/', '\\');

            foreach (var recipe in recipes)
            {
                foreach (var patch in recipe.Patches)
                {
                    string targetFile = $"{rootPath}/{recipe.TargetResource}/{patch.TargetFilePath}";

                    try
                    {
                        string content = await provider.ReadAllTextAsync(targetFile);
                        string original = content;

                        if (ApplyPatch(ref content, patch, log))
                        {
                            if (content != original)
                            {
                                await provider.CreateBackupAsync(targetFile);
                                await provider.WriteAllTextAsync(targetFile, content);
                                audit.LogChange(targetFile, $"Applied Patch for {recipe.RecipeId}", recipe.Description);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        log.LogWrite($"[ERROR] Failed to apply patch for {recipe.RecipeId} to {targetFile}: {ex.Message}");
                    }
                }
            }

            string auditReport = audit.GenerateReport();
            string auditFile = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tgtoolkit_audit.txt");
            System.IO.File.WriteAllText(auditFile, auditReport);
        }
    }
}
