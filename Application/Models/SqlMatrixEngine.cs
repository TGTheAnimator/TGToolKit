using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ToolKitV.Models.Providers;

namespace ToolKitV.Models
{
    /// <summary>
    /// Represents a parsed table requirement found in a resource's .sql file.
    /// </summary>
    public class SqlRequirement
    {
        public string ResourceName        { get; set; } = string.Empty;
        public string TableName           { get; set; } = string.Empty;
        public string RawCreateStatement  { get; set; } = string.Empty;
        public bool   ExistsInLiveDb      { get; set; }
        public string SourceFile          { get; set; } = string.Empty;
        public string FullFilePath        { get; set; } = string.Empty;

        // Visual properties for WPF DataGrid binding
        public string StatusText  => ExistsInLiveDb ? "Active in DB" : "Missing";
        public string StatusColor => ExistsInLiveDb ? "#4CFF70" : "#FF5555";
    }

    /// <summary>
    /// Recursive scanner and regex parser engine for FiveM SQL schemas.
    /// </summary>
    public static class SqlMatrixEngine
    {
        // Ignore list for massive standard base system schemas that are loaded during initial installation
        private static readonly HashSet<string> IgnoredSqlFiles = new(StringComparer.OrdinalIgnoreCase)
        {
            "qbcore.sql", "esx_legacy.sql", "es_extended.sql", "qbox.sql", "ox_inventory.sql"
        };

        /// <summary>
        /// Recursively scans for .sql files, extracts CREATE TABLE statements using optimized regex,
        /// and compares results with the live database.
        /// </summary>
        public static async Task<List<SqlRequirement>> ScanAndCompareAsync(
            IFileSystemProvider fs,
            string serverRootPath,
            DatabaseProvider db,
            IProgress<string>? progress = null)
        {
            var requirements = new List<SqlRequirement>();

            // 1. Retrieve the list of live tables
            progress?.Report("Querying live tables from active database...");
            HashSet<string> liveTables;
            try
            {
                liveTables = await db.GetLiveTablesAsync();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Could not fetch tables from active database:\n{ex.Message}", ex);
            }

            // 2. Discover all SQL files inside the directory
            progress?.Report("Scanning directory for SQL schema scripts...");
            var sqlFiles = await fs.DiscoverFilesAsync(serverRootPath, ".sql");

            int scannedCount = 0;
            // 3. Process discovered SQL files
            foreach (var filePath in sqlFiles)
            {
                string fileName = Path.GetFileName(filePath);
                scannedCount++;

                // Skip ignored large framework templates
                if (IgnoredSqlFiles.Contains(fileName))
                {
                    progress?.Report($"Skipping framework template: {fileName}...");
                    continue;
                }

                progress?.Report($"Parsing SQL file ({scannedCount}/{sqlFiles.Count}): {fileName}...");

                try
                {
                    string sqlText = await fs.ReadAllTextAsync(filePath);
                    if (string.IsNullOrWhiteSpace(sqlText)) continue;

                    // Regex matches: CREATE TABLE [IF NOT EXISTS] `tablename` or tablename (
                    var matches = Regex.Matches(sqlText, @"CREATE\s+TABLE\s+(?:IF\s+NOT\s+EXISTS\s+)?`?([a-zA-Z0-9_\-]+)`?\s*\(", RegexOptions.IgnoreCase);

                    foreach (Match match in matches)
                    {
                        if (match.Groups.Count < 2) continue;

                        string tableName = match.Groups[1].Value.Trim();
                        if (string.IsNullOrEmpty(tableName)) continue;

                        // Check if it already exists
                        bool exists = liveTables.Contains(tableName);

                        // Extract the full raw CREATE TABLE statement up to the ending semicolon
                        int startIndex = match.Index;
                        int endIndex = sqlText.IndexOf(';', startIndex);
                        string rawStatement = string.Empty;

                        if (endIndex > startIndex)
                        {
                            rawStatement = sqlText.Substring(startIndex, endIndex - startIndex + 1).Trim();
                        }
                        else
                        {
                            // If no semicolon, take up to 800 chars or end of file
                            int length = Math.Min(800, sqlText.Length - startIndex);
                            rawStatement = sqlText.Substring(startIndex, length).Trim() + "\n... [truncated]";
                        }

                        string resourceName = ExtractResourceName(filePath);

                        requirements.Add(new SqlRequirement
                        {
                            ResourceName       = resourceName,
                            TableName          = tableName,
                            RawCreateStatement = rawStatement,
                            ExistsInLiveDb     = exists,
                            SourceFile         = fileName,
                            FullFilePath       = filePath
                        });
                    }
                }
                catch
                {
                    // Ignore individual parse errors (e.g. locks or binary files) to ensure scanning robustness
                }
            }

            progress?.Report($"Scan finished. Found {requirements.Count} table requirement(s).");
            return requirements;
        }

        /// <summary>
        /// FiveM-aware resource folder name extractor.
        /// Accounts for nested categories like "[qb]", "[standalone]", etc.
        /// </summary>
        private static string ExtractResourceName(string path)
        {
            string norm = path.Replace('\\', '/');
            int idx = norm.IndexOf("/resources/", StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                string sub = norm[(idx + "/resources/".Length)..];
                string[] parts = sub.Split('/', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length > 0)
                {
                    // If first directory starts with [ and ends with ], it's a category
                    if (parts[0].StartsWith('[') && parts[0].EndsWith(']') && parts.Length > 1)
                    {
                        return parts[1];
                    }
                    return parts[0];
                }
            }

            // Fallback to immediate parent directory name
            string[] allParts = norm.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (allParts.Length >= 2)
            {
                return allParts[allParts.Length - 2];
            }
            return "unknown";
        }
    }
}
