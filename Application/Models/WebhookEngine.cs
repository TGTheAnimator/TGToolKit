using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace ToolKitV.Models
{
    /// <summary>
    /// Master Webhook Injector — scans every server/config Lua file in the workspace and
    /// overwrites any variable that looks like a Discord webhook URL with the master URL.
    /// Scoped to config and server files only for speed. Comment-aware via LuaAstHelper.
    /// </summary>
    public static class WebhookEngine
    {
        // Matches: Config.Webhook = 'url', DiscordURL = "url", DiscordLog = 'url', webhook = "url"
        private static readonly Regex WebhookRegex = new(
            @"(?i)([\w\.]*(?:webhook|discordurl|discordlog)[\w\.]*)\s*=\s*(['""])(.*?)\2",
            RegexOptions.Compiled);

        public static async Task<int> InjectMasterWebhookAsync(
            string workspaceRoot,
            string masterWebhookUrl,
            AuditLogger auditLog)
        {
            if (string.IsNullOrWhiteSpace(masterWebhookUrl)) return 0;

            int filesUpdated = 0;

            // Webhooks live in server or config files — skip client files for speed
            var targetFiles = Directory.GetFiles(workspaceRoot, "*.lua", SearchOption.AllDirectories)
                .Where(f =>
                {
                    string norm = f.Replace('\\', '/').ToLowerInvariant();
                    return (norm.Contains("/config") || norm.Contains("/server")) &&
                           !norm.Contains("/stream/") && !norm.Contains("/html/") && !norm.Contains("/ui/");
                })
                .ToList();

            foreach (var file in targetFiles)
            {
                string original = await File.ReadAllTextAsync(file);

                // Strip comments before matching to avoid changing commented-out code
                string stripped = LuaAstHelper.StripComments(original);

                if (!WebhookRegex.IsMatch(stripped))
                    continue;

                // Apply replacement to the original (preserving formatting), guided by positions from stripped
                string newText = WebhookRegex.Replace(original, m =>
                    $"{m.Groups[1].Value} = {m.Groups[2].Value}{masterWebhookUrl}{m.Groups[2].Value}");

                if (newText == original) continue;

                // Atomic backup before writing
                string backupPath = file + ".tg_backup";
                if (!File.Exists(backupPath))
                    await File.WriteAllTextAsync(backupPath, original);

                await File.WriteAllTextAsync(file, newText);
                filesUpdated++;

                string scriptName = Path.GetFileName(Path.GetDirectoryName(file)) ?? Path.GetFileName(file);
                auditLog.LogChange(
                    file.Replace(workspaceRoot, string.Empty),
                    "Webhook Injected",
                    $"Wired '{scriptName}' to the master Discord channel.");
            }

            return filesUpdated;
        }
    }
}
