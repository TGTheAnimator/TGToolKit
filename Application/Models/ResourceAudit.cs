using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace ToolKitV.Models
{
    public enum FileCategory
    {
        Texture,
        Model,
        Collision,
        Audio,
        Other
    }

    public enum DangerLevel
    {
        Safe,
        Warning,
        Critical
    }

    public class FileAuditEntry
    {
        public string FileName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public FileCategory Category { get; set; }
        public float VirtualMB { get; set; }
        public float DiskMB { get; set; }
        public DangerLevel Status { get; set; }
        public string? Recommendation { get; set; }
    }

    public class AuditResult
    {
        public string ResourceName { get; set; } = string.Empty;
        public string ResourcePath { get; set; } = string.Empty;
        public float TextureVirtualMB { get; set; }
        public float ModelVirtualMB { get; set; }
        public float CollisionVirtualMB { get; set; }
        public float AudioDiskMB { get; set; }
        public float OtherVirtualMB { get; set; }
        public float TotalEstimatedMB => TextureVirtualMB + ModelVirtualMB + CollisionVirtualMB + AudioDiskMB + OtherVirtualMB;
        public List<FileAuditEntry> Files { get; set; } = new();
        public DangerLevel OverallStatus { get; set; }
        public List<string> Recommendations { get; set; } = new();
    }

    public static class ResourceAudit
    {
        public static AuditResult AuditResource(string resourcePath, Action<int, int, int>? progressHandler = null)
        {
            var result = new AuditResult
            {
                ResourceName = Path.GetFileName(resourcePath),
                ResourcePath = resourcePath
            };

            var allFiles = Directory.GetFiles(resourcePath, "*.*", SearchOption.AllDirectories);
            int total = allFiles.Length;
            int current = 0;

            foreach (var file in allFiles)
            {
                current++;
                progressHandler?.Invoke((int)((current / (float)total) * 100), current, total);

                var ext = Path.GetExtension(file).ToLowerInvariant();
                var entry = new FileAuditEntry
                {
                    FileName = Path.GetFileName(file),
                    FilePath = file
                };

                if (ext == ".awc")
                {
                    entry.Category = FileCategory.Audio;
                    entry.DiskMB = new FileInfo(file).Length / 1024f / 1024f;
                    entry.VirtualMB = 0f; // AWC doesn't have an RSC7 header

                    if (entry.DiskMB > 10.0f)
                    {
                        entry.Status = DangerLevel.Warning;
                        entry.Recommendation = "Large soundbank detected. Extract via OpenIV → re-encode to 44.1kHz mono ADPCM → reimport.";
                    }
                    else
                    {
                        entry.Status = DangerLevel.Safe;
                    }

                    result.AudioDiskMB += entry.DiskMB;
                }
                else if (ext == ".ytd" || ext == ".yft" || ext == ".ydr" || ext == ".ydd" || ext == ".ybn" || ext == ".ypt" || ext == ".ycd")
                {
                    var (vMB, diskMB) = Rsc7SizeHelper.GetFileSize(file);
                    entry.VirtualMB = vMB;
                    entry.DiskMB = diskMB;

                    if (ext == ".ytd")
                    {
                        entry.Category = FileCategory.Texture;
                        result.TextureVirtualMB += vMB;

                        if (vMB > 16.0f)
                        {
                            entry.Status = DangerLevel.Critical;
                            entry.Recommendation = "Use Texture Optimizer to compress and downscale this YTD.";
                        }
                    }
                    else if (ext == ".yft" || ext == ".ydr" || ext == ".ydd")
                    {
                        entry.Category = FileCategory.Model;
                        result.ModelVirtualMB += vMB;

                        if (vMB > 16.0f)
                        {
                            entry.Status = DangerLevel.Critical;
                            entry.Recommendation = "Model exceeds streaming limit. Reduce polygon count in a 3D editor.";
                        }
                    }
                    else if (ext == ".ybn")
                    {
                        entry.Category = FileCategory.Collision;
                        result.CollisionVirtualMB += vMB;
                    }
                    else
                    {
                        entry.Category = FileCategory.Other;
                        result.OtherVirtualMB += vMB;
                    }
                }
                else
                {
                    continue; // Skip xml, lua, meta, etc. for budget calculations
                }

                if (entry.Status != DangerLevel.Safe && !string.IsNullOrEmpty(entry.Recommendation))
                {
                    if (!result.Recommendations.Contains(entry.Recommendation))
                    {
                        result.Recommendations.Add(entry.Recommendation);
                    }
                }

                result.Files.Add(entry);
            }

            int awcCount = result.Files.Count(f => f.Category == FileCategory.Audio);
            if (awcCount > 3)
            {
                result.Recommendations.Add("FiveM has a global 10-bank audio limit shared across all resources. Keep per-resource AWC count minimal.");
            }

            if (result.TotalEstimatedMB > 32.0f)
            {
                result.OverallStatus = DangerLevel.Critical;
                result.Recommendations.Insert(0, "Reduce YFT poly count and/or downscale YTD textures. Consider splitting into sub-resources.");
            }
            else if (result.TotalEstimatedMB > 20.0f)
            {
                result.OverallStatus = DangerLevel.Warning;
                result.Recommendations.Insert(0, "This resource is heavy. Optimize textures or reduce model complexity.");
            }
            else
            {
                result.OverallStatus = DangerLevel.Safe;
            }

            GenerateReport(result, resourcePath);

            return result;
        }

        private static void GenerateReport(AuditResult result, string directoryPath)
        {
            var sb = new StringBuilder();
            sb.AppendLine("═════════════════════════════════════════════════");
            sb.AppendLine($"TGToolKit — RESOURCE AUDIT REPORT: {result.ResourceName}");
            sb.AppendLine("═════════════════════════════════════════════════");
            sb.AppendLine($"Status: {result.OverallStatus}");
            sb.AppendLine($"Total Estimated Memory: {result.TotalEstimatedMB:F2} MB");
            sb.AppendLine("-------------------------------------------------");
            sb.AppendLine($"Textures (Virtual):  {result.TextureVirtualMB:F2} MB");
            sb.AppendLine($"Models (Virtual):    {result.ModelVirtualMB:F2} MB");
            sb.AppendLine($"Collision (Virtual): {result.CollisionVirtualMB:F2} MB");
            sb.AppendLine($"Audio (Disk):        {result.AudioDiskMB:F2} MB");
            sb.AppendLine($"Other (Virtual):     {result.OtherVirtualMB:F2} MB");
            sb.AppendLine("-------------------------------------------------");
            sb.AppendLine();

            if (result.Recommendations.Any())
            {
                sb.AppendLine("RECOMMENDATIONS:");
                foreach (var rec in result.Recommendations)
                {
                    sb.AppendLine($"-> {rec}");
                }
                sb.AppendLine();
            }

            var flaggedFiles = result.Files.Where(f => f.Status != DangerLevel.Safe).ToList();
            if (flaggedFiles.Any())
            {
                sb.AppendLine("FLAGGED FILES:");
                foreach (var file in flaggedFiles)
                {
                    sb.AppendLine($"[{file.Status.ToString().ToUpper()}] {file.FileName}");
                    if (file.VirtualMB > 0)
                        sb.AppendLine($"  Size: {file.VirtualMB:F2} MB Virtual | {file.DiskMB:F2} MB Disk");
                    else
                        sb.AppendLine($"  Size: {file.DiskMB:F2} MB Disk");
                    sb.AppendLine($"  -> {file.Recommendation}");
                    sb.AppendLine();
                }
            }

            string reportPath = Path.Combine(directoryPath, "resource_audit_report.txt");
            try
            {
                File.WriteAllText(reportPath, sb.ToString(), Encoding.UTF8);
            }
            catch { /* Best effort */ }
        }
    }
}
