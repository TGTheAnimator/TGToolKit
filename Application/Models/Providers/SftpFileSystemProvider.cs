#pragma warning disable CS0618   // GiveUpSecurityAndAcceptAnySshHostKey is obsolete but required
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using WinSCP;

namespace ToolKitV.Models.Providers
{
    /// <summary>
    /// WinSCP .NET assembly implementation of <see cref="IFileSystemProvider"/>.
    /// Maintains a single persistent session for the lifetime of an Auto-Wirer /
    /// Conflict Resolver operation. Uses server-side <c>cp</c> for fast backups.
    /// </summary>
    public class SftpFileSystemProvider : IFileSystemProvider
    {
        private Session? _session;
        private readonly SessionOptions _opts;

        public SftpFileSystemProvider(string host, int port, string username, string password)
        {
            _opts = new SessionOptions
            {
                Protocol                           = Protocol.Sftp,
                HostName                           = host,
                PortNumber                         = port,
                UserName                           = username,
                Password                           = password,
                GiveUpSecurityAndAcceptAnySshHostKey = true,
                TimeoutInMilliseconds              = 20_000,
            };

            try
            {
                _session = SftpManifestProvider.OpenSession(_opts);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Cannot connect to SFTP at {host}:{port}\n\n{ex.Message}", ex);
            }
        }

        private Session S => _session ?? throw new ObjectDisposedException(nameof(SftpFileSystemProvider));

        /// <summary>Normalises any path to Linux forward-slashes before passing to WinSCP.</summary>
        private static string N(string p) => p.Replace('\\', '/');

        // ── Traversal ─────────────────────────────────────────────────────────────

        public Task<List<string>> DiscoverFilesAsync(string rootPath, string searchPattern)
        {
            return Task.Run(() =>
            {
                // EnumerateRemoteFiles handles recursion natively — no manual traversal needed
                string mask = searchPattern.StartsWith('*') ? searchPattern : $"*{searchPattern}";

                return S.EnumerateRemoteFiles(N(rootPath), mask, WinSCP.EnumerationOptions.AllDirectories)
                    .Select(f => N(f.FullName))
                    .ToList();
            });
        }

        // ── I/O ───────────────────────────────────────────────────────────────────

        public Task<string> ReadAllTextAsync(string remotePath)
        {
            return Task.Run(() =>
            {
                string tmp = Path.GetTempFileName();
                try
                {
                    S.GetFiles(N(remotePath), tmp).Check();
                    return File.ReadAllText(tmp);
                }
                finally { try { File.Delete(tmp); } catch { } }
            });
        }

        public Task WriteAllTextAsync(string remotePath, string content)
        {
            return Task.Run(() =>
            {
                string tmp = Path.GetTempFileName();
                try
                {
                    File.WriteAllText(tmp, content, System.Text.Encoding.UTF8);
                    S.PutFiles(tmp, N(remotePath)).Check();
                }
                finally { try { File.Delete(tmp); } catch { } }
            });
        }

        // ── Safety ────────────────────────────────────────────────────────────────

        public Task CreateBackupAsync(string originalPath)
        {
            return Task.Run(() =>
            {
                string backupPath = N(originalPath) + ".tg_backup";
                string normOriginal = N(originalPath);

                if (S.FileExists(backupPath)) return;

                try
                {
                    // Server-side copy — no data transfer to local machine, instant
                    S.ExecuteCommand($"cp \"{normOriginal}\" \"{backupPath}\"");
                }
                catch
                {
                    // Fallback: download → re-upload
                    try
                    {
                        string tmp = Path.GetTempFileName();
                        try
                        {
                            S.GetFiles(originalPath, tmp).Check();
                            S.PutFiles(tmp, backupPath).Check();
                        }
                        finally { try { File.Delete(tmp); } catch { } }
                    }
                    catch { /* best-effort — never block a fix */ }
                }
            });
        }

        public Task DeleteFileAsync(string remotePath)
            => Task.Run(() => S.RemoveFile(N(remotePath)));

        public Task RenameDirectoryAsync(string oldPath, string newPath)
            => Task.Run(() => S.MoveFile(N(oldPath), N(newPath)));

        public Task DownloadDirectoryAsync(string remotePath, string localTempPath)
        {
            return Task.Run(() =>
            {
                var transferOptions = new TransferOptions { TransferMode = TransferMode.Binary };
                TransferOperationResult result = S.GetFiles(N(remotePath), localTempPath, false, transferOptions);
                result.Check();
            });
        }

        public Task UploadFileAsync(string localFilePath, string remoteFilePath)
        {
            return Task.Run(() =>
            {
                var transferOptions = new TransferOptions { TransferMode = TransferMode.Binary };
                TransferOperationResult result = S.PutFiles(localFilePath, N(remoteFilePath), false, transferOptions);
                result.Check();
            });
        }

        // ── Lifecycle ─────────────────────────────────────────────────────────────

        public void Disconnect()
        {
            try { if (_session?.Opened == true) _session.Close(); }
            finally { _session?.Dispose(); _session = null; }
        }
    }
}
#pragma warning restore CS0618
