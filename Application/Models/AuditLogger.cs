using System;
using System.Collections.Generic;
using System.Text;

namespace ToolKitV.Models
{
    public class AuditEntry
    {
        public string FilePath { get; set; } = string.Empty;
        public string ActionTaken { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
    }

    public class AuditLogger
    {
        private readonly List<AuditEntry> _entries = new List<AuditEntry>();

        public void LogChange(string path, string action, string reason)
        {
            _entries.Add(new AuditEntry { FilePath = path, ActionTaken = action, Reason = reason });
        }

        public string GenerateReport()
        {
            var sb = new StringBuilder();
            sb.AppendLine("==================================================");
            sb.AppendLine("TGToolKit Omni-Wirer Audit Log");
            sb.AppendLine($"Timestamp: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine("==================================================\n");

            if (_entries.Count == 0)
            {
                sb.AppendLine("No configuration changes were required.");
                return sb.ToString();
            }

            foreach (var entry in _entries)
            {
                sb.AppendLine($"[FILE]   {entry.FilePath}");
                sb.AppendLine($"[ACTION] {entry.ActionTaken}");
                sb.AppendLine($"[REASON] {entry.Reason}");
                sb.AppendLine(new string('-', 50));
            }

            return sb.ToString();
        }
    }
}
