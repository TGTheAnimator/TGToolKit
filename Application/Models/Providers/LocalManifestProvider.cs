using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace ToolKitV.Models.Providers
{
    /// <summary>
    /// Local file-system implementation of <see cref="IManifestProvider"/>.
    /// Walks the resource tree intelligently, skipping bracket category folders
    /// ([cars], [scripts], etc.) without descending blindly.
    /// </summary>
    public class LocalManifestProvider : IManifestProvider
    {
        public Task<List<(string ResourceName, string Content)>> GetManifestsAsync(string rootPath)
        {
            return Task.Run(() =>
            {
                var results = new List<(string, string)>();

                foreach (string dir in Directory.EnumerateDirectories(rootPath, "*", SearchOption.AllDirectories))
                {
                    string dirName = Path.GetFileName(dir);

                    // Skip category brackets (e.g. [cars]) — they are not resources themselves
                    if (dirName.StartsWith('[') && dirName.EndsWith(']'))
                        continue;

                    foreach (string manifest in new[] { "fxmanifest.lua", "__resource.lua" })
                    {
                        string manifestPath = Path.Combine(dir, manifest);
                        if (!File.Exists(manifestPath)) continue;

                        try
                        {
                            string content = File.ReadAllText(manifestPath);
                            results.Add((dirName, content));
                        }
                        catch (Exception ex)
                        {
                            results.Add((dirName, $"-- ERROR reading manifest: {ex.Message}"));
                        }

                        break; // Only one manifest per resource
                    }
                }

                return results;
            });
        }

        public Task<Dictionary<string, List<string>>> GetStreamFileMapAsync(string rootPath)
        {
            return Task.Run(() =>
            {
                var map = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

                foreach (string dir in Directory.EnumerateDirectories(rootPath, "*", SearchOption.AllDirectories))
                {
                    string streamDir = Path.Combine(dir, "stream");
                    if (!Directory.Exists(streamDir)) continue;

                    string resourceName = Path.GetFileName(dir);
                    var files = new List<string>();

                    foreach (string file in Directory.EnumerateFiles(streamDir, "*", SearchOption.AllDirectories))
                        files.Add(Path.GetFileName(file));

                    if (files.Count > 0)
                        map[resourceName] = files;
                }

                return map;
            });
        }
    }
}
