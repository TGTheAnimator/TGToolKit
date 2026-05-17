using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace ToolKitV.Models.Providers
{
    /// <summary>
    /// Local-disk implementation of <see cref="IFileSystemProvider"/>.
    /// Wraps <c>System.IO</c> directly — no network I/O.
    /// </summary>
    public class LocalFileSystemProvider : IFileSystemProvider
    {
        public Task<List<string>> DiscoverFilesAsync(string rootPath, string searchPattern)
        {
            return Task.Run(() =>
            {
                var results = new List<string>();

                if (!Directory.Exists(rootPath)) return results;

                // Exact filename match (e.g. "fxmanifest.lua")
                // Wildcard match   (e.g. "*.lua")
                try
                {
                    var found = Directory.GetFiles(rootPath, searchPattern, SearchOption.AllDirectories);
                    results.AddRange(found);
                }
                catch { /* Permission-denied folders are silently skipped */ }

                return results;
            });
        }

        public Task<string> ReadAllTextAsync(string path)
            => File.ReadAllTextAsync(path);

        public Task WriteAllTextAsync(string path, string content)
            => File.WriteAllTextAsync(path, content, System.Text.Encoding.UTF8);

        public Task CreateBackupAsync(string originalPath)
        {
            return Task.Run(() =>
            {
                try
                {
                    string backupPath = originalPath + ".tg_backup";
                    if (!File.Exists(backupPath))
                        File.Copy(originalPath, backupPath);
                }
                catch { /* Best-effort — never block a fix due to backup failure */ }
            });
        }

        public Task DeleteFileAsync(string path)
            => Task.Run(() => File.Delete(path));

        public Task RenameDirectoryAsync(string oldPath, string newPath)
            => Task.Run(() => Directory.Move(oldPath, newPath));

        /// <summary>No-op for local provider — no connection to release.</summary>
        public void Disconnect() { }

        public Task DownloadDirectoryAsync(string remotePath, string localTempPath)
        {
            return Task.Run(() =>
            {
                string source = remotePath;
                if (source.EndsWith("\\*") || source.EndsWith("/*"))
                {
                    source = source.Substring(0, source.Length - 2);
                }

                CopyDirectory(source, localTempPath);
            });
        }

        public Task UploadFileAsync(string localFilePath, string remoteFilePath)
        {
            return Task.Run(() =>
            {
                string? dir = Path.GetDirectoryName(remoteFilePath);
                if (dir != null) Directory.CreateDirectory(dir);
                File.Copy(localFilePath, remoteFilePath, true);
            });
        }

        private static void CopyDirectory(string sourceDir, string targetDir)
        {
            Directory.CreateDirectory(targetDir);

            foreach (var file in Directory.GetFiles(sourceDir))
            {
                string dest = Path.Combine(targetDir, Path.GetFileName(file));
                File.Copy(file, dest, true);
            }

            foreach (var subDir in Directory.GetDirectories(sourceDir))
            {
                string dest = Path.Combine(targetDir, Path.GetFileName(subDir));
                CopyDirectory(subDir, dest);
            }
        }
    }
}
