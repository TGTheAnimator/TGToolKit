using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace ToolKitV.Models
{
    public class LinterIgnoreData
    {
        // Global ignores map directly to warning signatures (e.g., "Missing game declaration")
        public HashSet<string> GlobalIgnores { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        
        // Resource-specific ignores: ResourceName -> Hashset of Signatures
        public Dictionary<string, HashSet<string>> ResourceIgnores { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public class LinterIgnoreManager
    {
        private readonly string _ignoreFilePath;
        private LinterIgnoreData _data = new();

        public LinterIgnoreManager(string localWorkspaceRoot)
        {
            _ignoreFilePath = AppPaths.GlobalIgnoreFilePath;
            LoadIgnores();
        }

        private void LoadIgnores()
        {
            if (File.Exists(_ignoreFilePath))
            {
                try
                {
                    string json = File.ReadAllText(_ignoreFilePath);
                    _data = JsonSerializer.Deserialize<LinterIgnoreData>(json) ?? new LinterIgnoreData();
                }
                catch
                {
                    _data = new LinterIgnoreData();
                }
            }
        }

        public async Task IgnoreIssueAsync(string resourceName, string warningSignature, bool global)
        {
            if (global)
            {
                _data.GlobalIgnores.Add(warningSignature);
            }
            else
            {
                if (!_data.ResourceIgnores.ContainsKey(resourceName))
                {
                    _data.ResourceIgnores[resourceName] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                }
                _data.ResourceIgnores[resourceName].Add(warningSignature);
            }

            var options = new JsonSerializerOptions { WriteIndented = true };
            string json = JsonSerializer.Serialize(_data, options);
            await File.WriteAllTextAsync(_ignoreFilePath, json);
        }

        public bool IsIgnored(string resourceName, string warningSignature)
        {
            if (_data.GlobalIgnores.Contains(warningSignature))
            {
                return true;
            }

            if (_data.ResourceIgnores.TryGetValue(resourceName, out var signatures))
            {
                if (signatures.Contains(warningSignature))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
