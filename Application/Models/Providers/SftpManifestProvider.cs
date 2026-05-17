#pragma warning disable CS0618   // GiveUpSecurityAndAcceptAnySshHostKey is obsolete but required
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using WinSCP;

namespace ToolKitV.Models.Providers
{
    /// <summary>
    /// WinSCP .NET assembly implementation of <see cref="IManifestProvider"/>.
    /// Uses <c>GiveUpSecurityAndAcceptAnySshHostKey</c> to bypass SSH host-key
    /// fingerprint blocking — required for Pterodactyl / Apollo Panel servers that
    /// regenerate keys on every rebuild.
    /// </summary>
    public class SftpManifestProvider : IManifestProvider
    {
        private readonly SessionOptions _opts;

        public SftpManifestProvider(string host, int port, string username, string password)
        {
            _opts = new SessionOptions
            {
                Protocol                           = Protocol.Sftp,
                HostName                           = host,
                PortNumber                         = port,
                UserName                           = username,
                Password                           = password,
                GiveUpSecurityAndAcceptAnySshHostKey = true,   // bypass key fingerprint check
                TimeoutInMilliseconds              = 20_000,
            };
        }

        public async Task<List<(string ResourceName, string Content)>> GetManifestsAsync(string rootPath)
        {
            return await Task.Run(() =>
            {
                var results = new List<(string, string)>();
                using var session = OpenSession();

                string root = rootPath.TrimEnd('/');

                // Phase 1 — eagerly collect all resource dirs (close all listing handles first)
                var dirs = CollectResourceDirs(session, root);

                // Phase 2 — download manifest files
                foreach (var (name, path) in dirs)
                {
                    foreach (string mf in new[] { "fxmanifest.lua", "__resource.lua" })
                    {
                        string remote = $"{path}/{mf}";
                        string tmp    = Path.GetTempFileName();
                        try
                        {
                            session.GetFiles(remote, tmp).Check();
                            results.Add((name, File.ReadAllText(tmp)));
                            break;
                        }
                        catch { /* file absent — try next name */ }
                        finally { try { File.Delete(tmp); } catch { } }
                    }
                }

                return results;
            });
        }

        public Task<Dictionary<string, List<string>>> GetStreamFileMapAsync(string rootPath)
            => Task.FromResult(new Dictionary<string, List<string>>());

        // ── Helpers ───────────────────────────────────────────────────────────────

        internal static Session OpenSession(SessionOptions opts)
        {
            var session = new Session
            {
                // Explicitly point to WinSCP.exe so the wrapper never searches in the
                // wrong directory (critical for single-file publish scenarios)
                ExecutablePath = FindWinScp()
            };
            session.Open(opts);
            return session;
        }

        private Session OpenSession() => OpenSession(_opts);

        private static List<(string Name, string FullPath)> CollectResourceDirs(
            Session session, string root)
        {
            // Resolve the correct SFTP path — Pterodactyl Wings chroots the session
            // so the real filesystem path "/home/container/resources" the user sees in
            // WinSCP GUI is NOT the same as the SFTP-accessible path (which is "/resources").
            string resolvedRoot = ResolvePath(session, root);

            var result = new List<(string, string)>();

            RemoteDirectoryInfo top;
            try   { top = session.ListDirectory(resolvedRoot); }
            catch (Exception ex)
            {
                throw new Exception(
                    $"Cannot list remote path: {root}\n\n{ex.Message}\n\n" +
                    "Tip: Pterodactyl/Wings chroots your session. If WinSCP shows\n" +
                    "\"/home/container/resources\", try entering just \"/resources\" instead.", ex);
            }

            foreach (RemoteFileInfo entry in top.Files)
            {
                if (!entry.IsDirectory || entry.Name == "." || entry.Name == "..") continue;

                if (entry.Name.StartsWith('[') && entry.Name.EndsWith(']'))
                {
                    string catPath = $"{resolvedRoot}/{entry.Name}";
                    try
                    {
                        var inner = session.ListDirectory(catPath);
                        foreach (RemoteFileInfo ie in inner.Files)
                            if (ie.IsDirectory && ie.Name != "." && ie.Name != "..")
                                result.Add((ie.Name, $"{catPath}/{ie.Name}"));
                    }
                    catch { }
                }
                else
                {
                    result.Add((entry.Name, $"{resolvedRoot}/{entry.Name}"));
                }
            }

            return result;
        }

        /// <summary>
        /// Tries the path as-is first. If the SFTP server returns "no such file",
        /// strips known Pterodactyl container path prefixes and retries.
        /// </summary>
        private static string ResolvePath(Session session, string path)
        {
            // 1. Try exact path
            if (PathExists(session, path)) return path;

            // 2. Strip Pterodactyl / Apollo Panel container prefixes
            string[] prefixes =
            {
                "/home/container",
                "/home/pterodactyl",
                "/srv/daemon-data",
                "/var/lib/pterodactyl",
            };

            foreach (string prefix in prefixes)
            {
                if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    string stripped = path[prefix.Length..];
                    if (string.IsNullOrEmpty(stripped)) stripped = "/";
                    if (PathExists(session, stripped)) return stripped;
                }
            }

            // 3. Try just the last path segment (e.g. "/resources" from any prefix)
            string leaf = "/" + path.TrimStart('/').Split('/')[^1];
            if (PathExists(session, leaf)) return leaf;

            // 4. Return original — CollectResourceDirs will throw a clear error
            return path;
        }

        private static bool PathExists(Session session, string path)
        {
            try { session.ListDirectory(path); return true; }
            catch { return false; }
        }

        internal static string FindWinScp()
        {
            // SingleFile publish: WinSCP.exe sits next to TGToolKit.exe
            string exeDir = Path.GetDirectoryName(Environment.ProcessPath)
                         ?? AppContext.BaseDirectory;

            foreach (string p in new[]
            {
                Path.Combine(exeDir, "WinSCP.exe"),
                Path.Combine(exeDir, "winscp.exe"),
                @"C:\Program Files (x86)\WinSCP\WinSCP.exe",
                @"C:\Program Files\WinSCP\WinSCP.exe",
            })
            {
                if (File.Exists(p)) return p;
            }

            throw new FileNotFoundException(
                "WinSCP.exe not found.\n\n" +
                "Place WinSCP.exe in the same folder as TGToolKit.exe, " +
                "or install WinSCP to its default location (C:\\Program Files (x86)\\WinSCP\\).\n\n" +
                "Download: https://winscp.net/eng/download.php");
        }
    }
}
#pragma warning restore CS0618
