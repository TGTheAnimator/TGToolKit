using Renci.SshNet;
using Renci.SshNet.Sftp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace ToolKitV.Models.Providers
{
    /// <summary>
    /// SSH.NET implementation of <see cref="IFileSystemProvider"/>.
    /// Targets RocketNode / Pterodactyl containers over SFTP.
    /// A single persistent <see cref="SftpClient"/> is reused for the lifetime of
    /// the Auto-Wire session and disposed via <see cref="Disconnect"/>.
    /// </summary>
    public class SftpFileSystemProvider : IFileSystemProvider
    {
        private readonly SftpClient _client;

        /// <summary>
        /// Opens and authenticates the SFTP connection immediately.
        /// Throws <see cref="Exception"/> if the credentials are wrong or the host
        /// is unreachable — the caller should catch and surface this in the UI.
        /// </summary>
        public SftpFileSystemProvider(string host, int port, string username, string password)
        {
            _client = new SftpClient(host, port, username, password);
            _client.Connect();
        }

        // ── Traversal ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Recursively walks the remote directory tree under <paramref name="rootPath"/>
        /// and returns every file whose name matches <paramref name="searchPattern"/>.
        /// <para>
        /// Pattern handling:
        /// <list type="bullet">
        ///   <item><c>"fxmanifest.lua"</c> — exact filename match</item>
        ///   <item><c>"*.lua"</c>          — any file ending in <c>.lua</c></item>
        /// </list>
        /// </para>
        /// </summary>
        public async Task<List<string>> DiscoverFilesAsync(string rootPath, string searchPattern)
        {
            var matchedFiles = new List<string>();

            // Convert glob pattern to a simple suffix/exact check
            string matchTerm = searchPattern.StartsWith("*")
                ? searchPattern[1..].ToLowerInvariant()   // "*.lua"  →  ".lua"
                : searchPattern.ToLowerInvariant();        // exact name

            await Task.Run(() => TraverseDirectory(rootPath, matchedFiles, matchTerm));
            return matchedFiles;
        }

        private void TraverseDirectory(string remotePath, List<string> matched, string matchTerm)
        {
            IEnumerable<ISftpFile> entries;
            try
            {
                entries = _client.ListDirectory(remotePath);
            }
            catch
            {
                return; // Inaccessible — permission denied or doesn't exist
            }

            foreach (var entry in entries)
            {
                if (entry.Name == "." || entry.Name == "..") continue;

                if (entry.IsDirectory)
                {
                    TraverseDirectory(entry.FullName, matched, matchTerm);
                }
                else if (entry.Name.ToLowerInvariant().EndsWith(matchTerm) ||
                         entry.Name.ToLowerInvariant() == matchTerm)
                {
                    matched.Add(entry.FullName);
                }
            }
        }

        // ── I/O ────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Downloads the remote file into an in-memory buffer and returns its text.
        /// No temporary files are written to the local disk.
        /// </summary>
        public async Task<string> ReadAllTextAsync(string remotePath)
        {
            return await Task.Run(() =>
            {
                using var ms = new MemoryStream();
                _client.DownloadFile(remotePath, ms);
                ms.Position = 0;
                using var reader = new StreamReader(ms, System.Text.Encoding.UTF8);
                return reader.ReadToEnd();
            });
        }

        /// <summary>
        /// Encodes <paramref name="content"/> as UTF-8 and streams it directly to
        /// the remote path via SFTP upload — no temp files, no disk I/O.
        /// </summary>
        public async Task WriteAllTextAsync(string remotePath, string content)
        {
            await Task.Run(() =>
            {
                using var ms = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));
                _client.UploadFile(ms, remotePath, true);
            });
        }

        // ── Safety ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Creates <c>{originalPath}.tg_backup</c> on the remote server.
        /// Uses a download-then-upload cycle because SFTP has no native copy command.
        /// Skips silently if the backup already exists (idempotent).
        /// </summary>
        public async Task CreateBackupAsync(string originalPath)
        {
            await Task.Run(() =>
            {
                string backupPath = originalPath + ".tg_backup";

                // Don't overwrite an existing backup — protects original state
                if (_client.Exists(backupPath)) return;

                try
                {
                    // Stream: remote source → memory → remote backup
                    using var ms = new MemoryStream();
                    _client.DownloadFile(originalPath, ms);
                    ms.Position = 0;
                    _client.UploadFile(ms, backupPath, false);
                }
                catch { /* Best-effort — never block a fix due to backup failure */ }
            });
        }

        // ── Lifecycle ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Cleanly closes the SFTP session and disposes the underlying SSH channel.
        /// Must be called when the Auto-Wire operation completes.
        /// </summary>
        public void Disconnect()
        {
            try
            {
                if (_client.IsConnected) _client.Disconnect();
            }
            finally
            {
                _client.Dispose();
            }
        }
    }
}
