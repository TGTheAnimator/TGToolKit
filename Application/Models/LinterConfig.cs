using System;
using System.IO;
using System.Text.Json;

namespace ToolKitV.Models
{
    /// <summary>
    /// Persists SFTP connection settings (host, port, username, root path) to a
    /// linter_config.json file in the app directory.
    /// Password is deliberately excluded and never written to disk.
    /// </summary>
    public class LinterConfig
    {
        public string Host     { get; set; } = string.Empty;
        public int    Port     { get; set; } = 22;
        public string Username { get; set; } = string.Empty;
        public string RootPath { get; set; } = "/home/container/resources";

        private static readonly string ConfigPath =
            AppPaths.LinterConfigFilePath;

        public static LinterConfig Load()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    string json = File.ReadAllText(ConfigPath);
                    return JsonSerializer.Deserialize<LinterConfig>(json) ?? new LinterConfig();
                }
            }
            catch { /* Return defaults on any error */ }

            return new LinterConfig();
        }

        public void Save()
        {
            try
            {
                string json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(ConfigPath, json);
            }
            catch { /* Best-effort — don't crash the app over config persistence */ }
        }
    }
}
