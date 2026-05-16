using CodeWalker.GameFiles;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ToolKitV.Models
{
    /// <summary>
    /// Analyzes .yft and .ydr files to detect oversized geometries that cause the
    /// FiveM "georgia-alaska-october" (GTA5_b3258.exe+7760CB) memory crash.
    /// Engine Limits:
    /// - Physical size > 16MB
    /// - Vertex count > 64,000 per drawable
    /// - Polygon count excessively high
    /// </summary>
    public static class ModelScanner
    {
        public const int MaxVerticesLimit = 64000;
        public const int WarningVerticesLimit = 50000;
        public const float MaxVirtualSizeLimitMB = 16.0f;
        public const float WarningVirtualSizeLimitMB = 10.0f;

        public enum DangerLevel
        {
            Safe,
            Warning,
            Critical // Will likely cause georgia-alaska-october crash
        }

        public class ModelStats
        {
            public string FilePath { get; set; } = string.Empty;
            public string FileName { get; set; } = string.Empty;
            public float DiskSizeMB { get; set; }
            public float VirtualSizeMB { get; set; }
            public int TotalVertices { get; set; }
            public int TotalPolygons { get; set; }
            public int HighestLODVertices { get; set; }
            public int HighestLODPolygons { get; set; }
            public DangerLevel Status { get; set; }
            public List<string> Issues { get; set; } = new();
        }

        public class ScanResults
        {
            public int TotalFilesScanned;
            public int SafeFiles;
            public int WarningFiles;
            public int CriticalFiles;
            public List<ModelStats> FlaggedModels = new();
            public TimeSpan ScanDuration;
        }

        public static ScanResults ScanDirectory(string directoryPath, Delegate progressHandler)
        {
            var results = new ScanResults();
            var startTime = DateTime.Now;

            var yftFiles = Directory.GetFiles(directoryPath, "*.yft", SearchOption.AllDirectories);
            var ydrFiles = Directory.GetFiles(directoryPath, "*.ydr", SearchOption.AllDirectories);
            var allFiles = yftFiles.Concat(ydrFiles).ToArray();

            results.TotalFilesScanned = allFiles.Length;

            if (allFiles.Length == 0)
                return results;

            var statsBag = new ConcurrentBag<ModelStats>();
            int processedCount = 0;

            Parallel.ForEach(allFiles, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, file =>
            {
                try
                {
                    var stats = AnalyzeModel(file);
                    statsBag.Add(stats);
                }
                catch
                {
                    // If we can't parse it, it's either corrupt or locked. We skip it so we don't crash the tool.
                }

                int count = System.Threading.Interlocked.Increment(ref processedCount);
                if (count % 10 == 0 || count == allFiles.Length)
                {
                    int progress = (int)((double)count / allFiles.Length * 100);
                    progressHandler?.DynamicInvoke(progress, count, allFiles.Length);
                }
            });

            foreach (var stat in statsBag)
            {
                if (stat.Status == DangerLevel.Safe)
                {
                    results.SafeFiles++;
                }
                else
                {
                    if (stat.Status == DangerLevel.Warning) results.WarningFiles++;
                    if (stat.Status == DangerLevel.Critical) results.CriticalFiles++;
                    results.FlaggedModels.Add(stat);
                }
            }

            // Sort flagged models by worst first (Critical -> Warning, then by highest vertices)
            results.FlaggedModels = results.FlaggedModels
                .OrderByDescending(x => x.Status)
                .ThenByDescending(x => x.HighestLODVertices)
                .ToList();

            results.ScanDuration = DateTime.Now - startTime;
            GenerateReport(directoryPath, results);

            return results;
        }

        private static ModelStats AnalyzeModel(string filePath)
        {
            var stats = new ModelStats
            {
                FilePath = filePath,
                FileName = Path.GetFileName(filePath)
            };

            var (vMB, diskMB) = Rsc7SizeHelper.GetFileSize(filePath);
            stats.VirtualSizeMB = vMB;
            stats.DiskSizeMB = diskMB;

            if (stats.VirtualSizeMB > MaxVirtualSizeLimitMB)
            {
                stats.Issues.Add($"Virtual size is {stats.VirtualSizeMB:F2} MB (Limit: {MaxVirtualSizeLimitMB:F2} MB). High risk of streaming failure. Reduce polygon count or split into LOD-separated YFTs. Target <10 MB virtual.");
                stats.Status = DangerLevel.Critical;
            }
            else if (stats.VirtualSizeMB > WarningVirtualSizeLimitMB)
            {
                stats.Issues.Add($"Virtual size is {stats.VirtualSizeMB:F2} MB. Very heavy, optimization recommended to prevent texture loss.");
                stats.Status = DangerLevel.Warning;
            }

            if (stats.DiskSizeMB > stats.VirtualSizeMB && stats.VirtualSizeMB > 0f)
            {
                stats.Issues.Add("Diagnostic: Disk size is larger than virtual size. Possible corrupted RSC header or unusual compression.");
            }

            byte[] data = File.ReadAllBytes(filePath);
            DrawableBase? drawable = null;

            var entry = new RpfResourceFileEntry { Name = Path.GetFileName(filePath) };

            if (filePath.EndsWith(".yft", StringComparison.OrdinalIgnoreCase))
            {
                var yft = RpfFile.GetFile<YftFile>(entry, data);
                drawable = yft?.Fragment?.Drawable;
            }
            else if (filePath.EndsWith(".ydr", StringComparison.OrdinalIgnoreCase))
            {
                var ydr = RpfFile.GetFile<YdrFile>(entry, data);
                drawable = ydr?.Drawable;
            }

            if (drawable != null)
            {
                AnalyzeDrawable(drawable, stats);
            }

            // Determine status based on vertices if size wasn't already critical
            if (stats.HighestLODVertices > MaxVerticesLimit)
            {
                stats.Issues.Add($"Geometry has {stats.HighestLODVertices:N0} vertices. EXCEEDS 64K LIMIT! Guaranteed georgia-alaska-october crash. LOD baking required. Use a DCC tool to reduce High LOD below 50k vertices.");
                stats.Status = DangerLevel.Critical;
            }
            else if (stats.HighestLODVertices > WarningVerticesLimit && stats.Status != DangerLevel.Critical)
            {
                stats.Issues.Add($"Geometry has {stats.HighestLODVertices:N0} vertices. Very close to the 64k engine limit. Consider reducing High LOD by 20–30% for performance headroom.");
                stats.Status = DangerLevel.Warning;
            }
            else if (stats.TotalPolygons > 100000 && stats.Status != DangerLevel.Critical)
            {
                stats.Issues.Add($"Extremely high poly count ({stats.TotalPolygons:N0}). Will cause severe FPS drops for clients.");
                stats.Status = DangerLevel.Warning;
            }

            return stats;
        }

        private static void AnalyzeDrawable(DrawableBase drawable, ModelStats stats)
        {
            if (drawable.DrawableModels?.High == null) return;

            // Usually, the 'High' LOD (Level of Detail) is what causes the crash when loading in close proximity.
            var highLOD = drawable.DrawableModels.High;
            
            foreach (var model in highLOD)
            {
                if (model.Geometries != null)
                {
                    foreach (var geom in model.Geometries)
                    {
                        stats.HighestLODVertices += (int)geom.VerticesCount;
                        stats.HighestLODPolygons += (int)(geom.IndicesCount / 3);
                    }
                }
            }

            // Calculate total across all LODs just for informational metrics
            if (drawable.DrawableModels != null)
            {
                CalculateModels(drawable.DrawableModels.High, stats);
                CalculateModels(drawable.DrawableModels.Med, stats);
                CalculateModels(drawable.DrawableModels.Low, stats);
                CalculateModels(drawable.DrawableModels.VLow, stats);
            }
        }

        private static void CalculateModels(DrawableModel[]? models, ModelStats stats)
        {
            if (models == null) return;
            foreach (var model in models)
            {
                if (model.Geometries == null) continue;
                foreach (var geom in model.Geometries)
                {
                    stats.TotalVertices += (int)geom.VerticesCount;
                    stats.TotalPolygons += (int)(geom.IndicesCount / 3);
                }
            }
        }

        private static void GenerateReport(string directoryPath, ScanResults results)
        {
            if (results.FlaggedModels.Count == 0) return;

            var sb = new StringBuilder();
            sb.AppendLine("TGToolKit — YFT Model Scan Report");
            sb.AppendLine("Resolves 'georgia-alaska-october' and memory-related game crashes.");
            sb.AppendLine(new string('─', 60));
            sb.AppendLine($"Scan Date:        {DateTime.Now}");
            sb.AppendLine($"Total Scanned:    {results.TotalFilesScanned} models");
            sb.AppendLine($"Safe Models:      {results.SafeFiles}");
            sb.AppendLine($"Warnings:         {results.WarningFiles}");
            sb.AppendLine($"Critical Failures: {results.CriticalFiles}");
            sb.AppendLine(new string('─', 60));
            sb.AppendLine();

            if (results.CriticalFiles > 0)
            {
                sb.AppendLine("🚨 CRITICAL MODELS (MUST FIX OR DELETE) 🚨");
                sb.AppendLine("These models exceed the hardcoded GTA V engine limits and WILL cause crashes for nearby players.");
                sb.AppendLine();
                foreach (var model in results.FlaggedModels.Where(m => m.Status == DangerLevel.Critical))
                {
                    sb.AppendLine($"[CRITICAL] {model.FileName}");
                    sb.AppendLine($"  Path:     {model.FilePath}");
                    sb.AppendLine($"  Size:     {model.VirtualSizeMB:F2} MB Virtual | {model.DiskSizeMB:F2} MB Disk");
                    sb.AppendLine($"  High LOD: {model.HighestLODVertices:N0} Vertices | {model.HighestLODPolygons:N0} Polygons");
                    foreach (var issue in model.Issues)
                        sb.AppendLine($"  -> {issue}");
                    sb.AppendLine();
                }
            }

            if (results.WarningFiles > 0)
            {
                sb.AppendLine("⚠️ WARNING MODELS (OPTIMIZATION RECOMMENDED) ⚠️");
                sb.AppendLine("These models are very heavy and may cause texture loss, FPS drops, or instability.");
                sb.AppendLine();
                foreach (var model in results.FlaggedModels.Where(m => m.Status == DangerLevel.Warning))
                {
                    sb.AppendLine($"[WARNING] {model.FileName}");
                    sb.AppendLine($"  Path:     {model.FilePath}");
                    sb.AppendLine($"  Size:     {model.VirtualSizeMB:F2} MB Virtual | {model.DiskSizeMB:F2} MB Disk");
                    sb.AppendLine($"  High LOD: {model.HighestLODVertices:N0} Vertices | {model.HighestLODPolygons:N0} Polygons");
                    foreach (var issue in model.Issues)
                        sb.AppendLine($"  -> {issue}");
                    sb.AppendLine();
                }
            }

            string reportPath = Path.Combine(directoryPath, "oversized_models_report.txt");
            try
            {
                File.WriteAllText(reportPath, sb.ToString(), Encoding.UTF8);
            }
            catch { /* Best effort */ }
        }
    }
}
