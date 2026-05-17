using System;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;

namespace ToolKitV.Models
{
    /// <summary>
    /// Phase 0 Temporal Failsafe — compresses the entire workspace into a timestamped
    /// zip archive before any destructive operation fires. Provides one-click full
    /// workspace restoration from any saved snapshot.
    /// </summary>
    public static class SnapshotEngine
    {
        public static string SnapshotDirectory =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Snapshots");

        /// <summary>
        /// Compresses localWorkspace into a timestamped zip archive under the app-local
        /// Snapshots/ directory. Returns the full path to the created zip file.
        /// </summary>
        public static async Task<string> CreateSafetySnapshotAsync(string localWorkspace, LogWriter? log = null)
        {
            log?.LogWrite("[SYSTEM] Initiating Phase 0: Temporal Failsafe...");

            Directory.CreateDirectory(SnapshotDirectory);

            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string zipPath   = Path.Combine(SnapshotDirectory, $"ServerState_{timestamp}.zip");

            // Run compression on a background thread so the UI stays responsive
            await Task.Run(() =>
                ZipFile.CreateFromDirectory(localWorkspace, zipPath, CompressionLevel.Fastest, false));

            long sizeMb = new FileInfo(zipPath).Length / (1024 * 1024);
            log?.LogWrite($"[SUCCESS] Pre-deployment snapshot secured: {Path.GetFileName(zipPath)} ({sizeMb} MB)");

            return zipPath;
        }

        /// <summary>
        /// Wipes the target workspace directory and extracts the selected snapshot zip into it.
        /// This is irreversible — callers must obtain explicit user confirmation before calling.
        /// </summary>
        public static async Task RestoreSnapshotAsync(string zipPath, string targetWorkspace, LogWriter? log = null)
        {
            if (!File.Exists(zipPath))
                throw new FileNotFoundException("Snapshot file not found.", zipPath);

            log?.LogWrite("[ROLLBACK] Purging corrupted workspace...");
            await Task.Run(() =>
            {
                if (Directory.Exists(targetWorkspace))
                    Directory.Delete(targetWorkspace, recursive: true);
            });

            log?.LogWrite("[ROLLBACK] Extracting clean snapshot...");
            await Task.Run(() => ZipFile.ExtractToDirectory(zipPath, targetWorkspace));

            log?.LogWrite("[SUCCESS] Workspace restored to pre-deployment state.");
        }

        /// <summary>
        /// Returns metadata for all snapshots in the Snapshots/ directory, newest first.
        /// </summary>
        public static SnapshotInfo[] ListSnapshots()
        {
            if (!Directory.Exists(SnapshotDirectory))
                return Array.Empty<SnapshotInfo>();

            return new DirectoryInfo(SnapshotDirectory)
                .GetFiles("*.zip")
                .OrderByDescending(static f => f.CreationTime)
                .Select(static f => new SnapshotInfo
                {
                    FilePath      = f.FullName,
                    FormattedDate = f.CreationTime.ToString("yyyy-MM-dd  HH:mm:ss"),
                    SizeMB        = Math.Round(f.Length / (1024.0 * 1024.0), 2)
                })
                .ToArray();
        }
    }

    public class SnapshotInfo
    {
        public string FilePath      { get; set; } = string.Empty;
        public string FormattedDate { get; set; } = string.Empty;
        public double SizeMB        { get; set; }
    }
}
