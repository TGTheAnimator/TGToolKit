using System;
using System.IO;
using System.Text.Json;
using System.Text;

namespace ToolKitV.Models
{
    /// <summary>
    /// Persists MySQL database connection credentials to db_config.json.
    /// Obfuscates the password using standard Base64 encoding to prevent cleartext exposure.
    /// </summary>
    public class DbConfig
    {
        public string Host     { get; set; } = "127.0.0.1";
        public int    Port     { get; set; } = 3306;
        public string Username { get; set; } = "root";
        public string Database { get; set; } = "fivem";

        // Internal password property serialized to JSON as obfuscated text
        public string PasswordObfuscated { get; set; } = string.Empty;

        // Public helper to get/set cleartext password
        [System.Text.Json.Serialization.JsonIgnore]
        public string Password
        {
            get
            {
                if (string.IsNullOrEmpty(PasswordObfuscated)) return string.Empty;
                try
                {
                    byte[] bytes = Convert.FromBase64String(PasswordObfuscated);
                    return Encoding.UTF8.GetString(bytes);
                }
                catch
                {
                    return string.Empty;
                }
            }
            set
            {
                if (string.IsNullOrEmpty(value))
                {
                    PasswordObfuscated = string.Empty;
                }
                else
                {
                    byte[] bytes = Encoding.UTF8.GetBytes(value);
                    PasswordObfuscated = Convert.ToBase64String(bytes);
                }
            }
        }

        private static readonly string ConfigPath =
            AppPaths.DbConfigFilePath;

        public static DbConfig Load()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    string json = File.ReadAllText(ConfigPath);
                    return JsonSerializer.Deserialize<DbConfig>(json) ?? new DbConfig();
                }
            }
            catch { /* Return defaults on error */ }

            return new DbConfig();
        }

        public void Save()
        {
            try
            {
                string json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(ConfigPath, json);
            }
            catch { /* Best effort persistence */ }
        }
    }
}
