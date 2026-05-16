using Renci.SshNet;
using Renci.SshNet.Sftp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace ToolKitV.Models.Providers
{
    /// <summary>
    /// SSH.NET implementation of <see cref="IManifestProvider"/>.
    /// Connects to a remote FiveM server over SFTP, streams only .lua manifest
    /// files — never downloads YTDs or large binaries.
    /// Properly disposes the SFTP session on completion.
    /// </summary>
    public class SftpManifestProvider : IManifestProvider
    {
        private readonly string _host;
        private readonly int    _port;
        private readonly string _username;
        private readonly string _password;

        public SftpManifestProvider(string host, int port, string username, string password)
        {
            _host     = host;
            _port     = port;
            _username = username;
            _password = password;
        }

        public async Task<List<(string ResourceName, string Content)>> GetManifestsAsync(string rootPath)
        {
            return await Task.Run(() =>
            {
                var results = new List<(string, string)>();

                using var client = CreateClient();
                client.Connect();

                // List all resource directories at root level (+ one level for bracket categories)
                foreach (var resourceDir in EnumerateResourceDirs(client, rootPath))
                {
                    string resourceName = resourceDir.Name;

                    foreach (string manifestName in new[] { "fxmanifest.lua", "__resource.lua" })
                    {
                        string remotePath = $"{resourceDir.FullName}/{manifestName}";
                        if (!client.Exists(remotePath)) continue;

                        try
                        {
                            using var stream = new MemoryStream();
                            client.DownloadFile(remotePath, stream);
                            stream.Position = 0;
                            string content = new StreamReader(stream).ReadToEnd();
                            results.Add((resourceName, content));
                        }
                        catch (Exception ex)
                        {
                            results.Add((resourceName, $"-- ERROR reading remote manifest: {ex.Message}"));
                        }
                        break; // Only one manifest per resource
                    }
                }

                client.Disconnect();
                return results;
            });
        }

        public async Task<Dictionary<string, List<string>>> GetStreamFileMapAsync(string rootPath)
        {
            return await Task.Run(() =>
            {
                var map = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

                using var client = CreateClient();
                client.Connect();

                foreach (var resourceDir in EnumerateResourceDirs(client, rootPath))
                {
                    string streamPath = $"{resourceDir.FullName}/stream";
                    if (!client.Exists(streamPath)) continue;

                    string resourceName = resourceDir.Name;
                    var files = new List<string>();

                    // Only list filenames — do NOT download
                    foreach (var file in client.ListDirectory(streamPath))
                    {
                        if (file.IsRegularFile)
                            files.Add(file.Name);
                    }

                    if (files.Count > 0)
                        map[resourceName] = files;
                }

                client.Disconnect();
                return map;
            });
        }

        // ─── Helpers ─────────────────────────────────────────────────────────────

        private SftpClient CreateClient()
            => new SftpClient(_host, _port, _username, _password);

        /// <summary>
        /// Enumerates all first-level resource directories under rootPath.
        /// Transparent to bracket category folders ([cars], [scripts], etc.) —
        /// descends one extra level into them to find the actual resources inside.
        /// </summary>
        private static IEnumerable<ISftpFile> EnumerateResourceDirs(SftpClient client, string rootPath)
        {
            IEnumerable<ISftpFile> topLevel;
            try
            {
                topLevel = client.ListDirectory(rootPath);
            }
            catch
            {
                yield break;
            }

            foreach (var entry in topLevel)
            {
                if (!entry.IsDirectory || entry.Name.StartsWith('.'))
                    continue;

                // Bracket category folder — descend one level
                if (entry.Name.StartsWith('[') && entry.Name.EndsWith(']'))
                {
                    IEnumerable<ISftpFile> inner;
                    try { inner = client.ListDirectory(entry.FullName); }
                    catch { continue; }

                    foreach (var innerEntry in inner)
                    {
                        if (innerEntry.IsDirectory && !innerEntry.Name.StartsWith('.'))
                            yield return innerEntry;
                    }
                }
                else
                {
                    yield return entry;
                }
            }
        }
    }
}
