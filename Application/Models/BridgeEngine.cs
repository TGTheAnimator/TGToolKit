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
                log?.LogWrite($"[CRITICAL] Regex timeout while attempting to patch {patch.TargetFilePath}. Pattern may be too broad.");
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

            // Create a local staging area that mirrors the remote file structure
            string stagingDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "TGToolKit_Staging_Bridge");
            if (System.IO.Directory.Exists(stagingDir)) System.IO.Directory.Delete(stagingDir, true);
            
            int filesPatched = 0;

            foreach (var recipe in recipes)
            {
                foreach (var patch in recipe.Patches)
                {
                    string remoteTargetFile = $"{rootPath}/{recipe.TargetResource}/{patch.TargetFilePath}";

                    try
                    {
                        // Note: We still have to read one-by-one to get the original text, 
                        // but we eliminate the slow Write transaction.
                        string content = await provider.ReadAllTextAsync(remoteTargetFile);
                        string original = content;

                        if (ApplyPatch(ref content, patch, log) && content != original)
                        {
                            // Mirror the remote path structure in our local staging folder
                            string relativeStructure = $"{recipe.TargetResource}/{patch.TargetFilePath}";
                            string localStagedFile = System.IO.Path.Combine(stagingDir, relativeStructure);
                            
                            string? localDir = System.IO.Path.GetDirectoryName(localStagedFile);
                            if (localDir != null) System.IO.Directory.CreateDirectory(localDir);
                            System.IO.File.WriteAllText(localStagedFile, content);
                            
                            await provider.CreateBackupAsync(remoteTargetFile);
                            audit.LogChange(remoteTargetFile, $"Applied Patch for {recipe.RecipeId}", recipe.Description);
                            filesPatched++;
                        }
                    }
                    catch (Exception ex)
                    {
                        log?.LogWrite($"[ERROR] Failed to apply patch for {recipe.RecipeId} to {remoteTargetFile}: {ex.Message}");
                    }
                }
            }

            // Blast all patched files back to the server in exactly ONE transaction
            if (filesPatched > 0)
            {
                log?.LogWrite($"[BRIDGE] Pipelining {filesPatched} patched files to the server...");
                await provider.UploadDirectoryBulkAsync(stagingDir, rootPath, log);
            }

            try { System.IO.Directory.Delete(stagingDir, true); } catch { }

            string auditReport = audit.GenerateReport();
            string auditFile = AppPaths.AuditLogFilePath; // Utilizing the new UAC compliant paths
            System.IO.File.WriteAllText(auditFile, auditReport);
        }
    }
}
