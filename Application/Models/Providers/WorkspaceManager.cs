using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace ToolKitV.Models.Providers
{
    public static class WorkspaceManager
    {
        public static async Task CloneWorkspaceTextFilesOnlyAsync(string sourceRoot, string tempTargetRoot)
        {
            var allowedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) 
            { 
                ".lua", ".cfg", ".meta", ".json" 
            };
            
            // Define forbidden directory names to skip processing them entirely
            var forbiddenFolders = new[] 
            { 
                "\\stream\\", "/stream/", "\\node_modules\\", "/node_modules/", "\\.git\\", "/.git/" 
            };

            await Task.Run(() =>
            {
                var allFiles = Directory.EnumerateFiles(sourceRoot, "*.*", SearchOption.AllDirectories);

                foreach (var file in allFiles)
                {
                    // 1. Instantly skip if it's inside a stream folder, node_modules, or git
                    if (forbiddenFolders.Any(folder => file.IndexOf(folder, StringComparison.OrdinalIgnoreCase) >= 0))
                        continue;

                    // 2. Instantly skip if it's an asset (PNG, YTD, YFT, MP3, etc.)
                    string ext = Path.GetExtension(file);
                    if (!allowedExtensions.Contains(ext))
                        continue;

                    // 3. Mirror the directory structure in the temp folder and copy the file
                    string relativePath = Path.GetRelativePath(sourceRoot, file);
                    string targetFilePath = Path.Combine(tempTargetRoot, relativePath);

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
