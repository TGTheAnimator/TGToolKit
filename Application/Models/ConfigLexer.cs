using System;
using System.IO;
using System.Threading.Tasks;
using MoonSharp.Interpreter;
using ToolKitV.Models.Providers;

namespace ToolKitV.Models
{
    public static class ConfigLexer
    {
        /// <summary>
        /// Safely evaluates a Lua config file locally and returns the exact value of a specified key.
        /// Example: GetConfigValue("config.lua", "Config", "Inventory") returns "ox"
        /// </summary>
        public static string? GetConfigValue(string filePath, string tableName, string keyName)
        {
            try
            {
                if (!File.Exists(filePath)) return null;
                string luaCode = File.ReadAllText(filePath);
                return EvaluateConfigValue(luaCode, tableName, keyName);
            }
            catch (Exception)
            {
                // Silence and return null to fallback to Regex parsing
            }
            return null;
        }

        /// <summary>
        /// Safely evaluates a Lua config file asynchronously from a provider (Local/SFTP) and returns the exact value of a specified key.
        /// </summary>
        public static async Task<string?> GetConfigValueAsync(IFileSystemProvider fs, string filePath, string tableName, string keyName)
        {
            try
            {
                string luaCode = await fs.ReadAllTextAsync(filePath);
                return EvaluateConfigValue(luaCode, tableName, keyName);
            }
            catch (Exception)
            {
                // Silence and return null to fallback to Regex parsing
            }
            return null;
        }

        private static string? EvaluateConfigValue(string luaCode, string tableName, string keyName)
        {
            try
            {
                Script script = new Script(CoreModules.Preset_SoftSandbox);
                
                // Stub out common CFX FiveM natives that config files frequently call
                script.DoString("vector2 = function(x, y) return { x = x, y = y } end");
                script.DoString("vector3 = function(x, y, z) return { x = x, y = y, z = z } end");
                script.DoString("vector4 = function(x, y, z, w) return { x = x, y = y, z = z, w = w } end");
                script.DoString("joaat = function(str) return 0 end");
                script.DoString("_U = function(str, ...) return str end");

                // Execute the config file in a sandbox to build the tables in memory
                script.DoString(luaCode);

                // Fetch the master table (e.g. "Config", "Shared", or "cfg")
                DynValue masterTable = script.Globals.Get(tableName);
                
                if (masterTable != null && masterTable.Type == DataType.Table)
                {
                    DynValue targetKey = masterTable.Table.Get(keyName);
                    
                    if (targetKey != null)
                    {
                        if (targetKey.Type == DataType.String) return targetKey.String;
                        if (targetKey.Type == DataType.Boolean) return targetKey.Boolean.ToString();
                        if (targetKey.Type == DataType.Number) return targetKey.Number.ToString();
                    }
                }
            }
            catch (Exception)
            {
                // Fall back gracefully
            }
            return null;
        }
    }
}
