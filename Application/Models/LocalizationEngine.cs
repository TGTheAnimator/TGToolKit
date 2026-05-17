using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace ToolKitV.Models
{
    /// <summary>
    /// Global Locale Synchronizer — updates Config.Locale / Config.Language across all config files,
    /// but ONLY if a matching translation file is proven to exist in that script's directory.
    /// Comment-aware via LuaAstHelper.
    /// </summary>
    public static class LocalizationEngine
    {
        // Matches: Config.Locale = 'en', Config.Language = "fr", Locale = 'es', etc.
        private static readonly Regex LocaleRegex = new(
            @"(?i)([\w\.]*(?:locale|language)[\w\.]*)\s*=\s*(['""])(.*?)\2",
            RegexOptions.Compiled);

        public static async Task<int> SyncAllLocalesAsync(
            string workspaceRoot,
            string targetLocale,
            AuditLogger auditLog)
        {
            if (string.IsNullOrWhiteSpace(targetLocale)) return 0;

            int scriptsLocalized = 0;

            var configFiles = Directory.GetFiles(workspaceRoot, "*.lua", SearchOption.AllDirectories)
                .Where(f =>
                {
                    string norm = f.Replace('\\', '/').ToLowerInvariant();
                    string name = Path.GetFileNameWithoutExtension(norm);
                    return name.Contains("config") &&
                           !norm.Contains("/stream/") && !norm.Contains("/html/") && !norm.Contains("/ui/");
                })
                .ToList();

            foreach (var file in configFiles)
            {
                string original = await File.ReadAllTextAsync(file);
                string stripped  = LuaAstHelper.StripComments(original);

                if (!LocaleRegex.IsMatch(stripped))
                    continue;

                // Validation Check: does a matching translation file exist in this script's tree?
                string scriptDir   = Path.GetDirectoryName(file) ?? workspaceRoot;
                bool localeExists  =
                    Directory.GetFiles(scriptDir, $"{targetLocale}.lua",  SearchOption.AllDirectories).Length > 0 ||
                    Directory.GetFiles(scriptDir, $"{targetLocale}.json", SearchOption.AllDirectories).Length > 0;

                string relPath = file.Replace(workspaceRoot, string.Empty);

                if (!localeExists)
                {
                    auditLog.LogChange(relPath,
                        "[WARNING] Locale Skip",
                        $"Attempted to set language to '{targetLocale}', but no matching translation file was found.");
                    continue;
                }

                string newText = LocaleRegex.Replace(original, m =>
                    $"{m.Groups[1].Value} = {m.Groups[2].Value}{targetLocale}{m.Groups[2].Value}");

                if (newText == original) continue;

                string backupPath = file + ".tg_backup";
                if (!File.Exists(backupPath))
                    await File.WriteAllTextAsync(backupPath, original);

                await File.WriteAllTextAsync(file, newText);
                scriptsLocalized++;

                auditLog.LogChange(relPath,
                    "Locale Synchronized",
                    $"Set language to '{targetLocale}'. Translation file verified.");
            }

            return scriptsLocalized;
        }
    }
}
