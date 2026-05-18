using System;
using System.IO;

namespace ToolKitV.Models
{
    public static class AppPaths
    {
        public static string RootDataFolder { get; }
        public static string SnapshotsFolder { get; }
        public static string RecipesFolder { get; }
        public static string GlobalRecipesFolder { get; }
        public static string LogsFolder { get; }

        // Specific Files
        public static string LinterConfigFilePath { get; }
        public static string AuditLogFilePath { get; }
        public static string ItemAuditLogFilePath { get; }
        public static string DbConfigFilePath { get; }
        public static string GlobalIgnoreFilePath { get; }
        public static string CrashLogFilePath { get; }

        static AppPaths()
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            RootDataFolder = Path.Combine(localAppData, "TGToolKit");

            // Subdirectories
            SnapshotsFolder = Path.Combine(RootDataFolder, "Snapshots");
            RecipesFolder = Path.Combine(RootDataFolder, "Recipes");
            GlobalRecipesFolder = Path.Combine(RootDataFolder, "GlobalRecipes");
            LogsFolder = Path.Combine(RootDataFolder, "Logs");

            // Files
            LinterConfigFilePath = Path.Combine(RootDataFolder, "linter_config.json");
            AuditLogFilePath = Path.Combine(LogsFolder, "tgtoolkit_audit.txt");
            ItemAuditLogFilePath = Path.Combine(LogsFolder, "tgtoolkit_item_audit.txt");
            DbConfigFilePath = Path.Combine(RootDataFolder, "db_config.json");
            GlobalIgnoreFilePath = Path.Combine(RootDataFolder, ".tgtoolkit_ignore.json");
            CrashLogFilePath = Path.Combine(LogsFolder, "crash.log");

            // Ensure directories exist immediately upon app startup
            EnsureDirectoriesExist();
        }

        private static void EnsureDirectoriesExist()
        {
            Directory.CreateDirectory(RootDataFolder);
            Directory.CreateDirectory(SnapshotsFolder);
            Directory.CreateDirectory(RecipesFolder);
            Directory.CreateDirectory(GlobalRecipesFolder);
            Directory.CreateDirectory(LogsFolder);

            // Copy seeded default recipes/configs from installation folder if not present in AppData
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            
            CopySeededDirectory(Path.Combine(baseDir, "Recipes"), RecipesFolder);
            CopySeededDirectory(Path.Combine(baseDir, "GlobalRecipes"), GlobalRecipesFolder);
        }

        private static void CopySeededDirectory(string sourceDir, string targetDir)
        {
            try
            {
                if (!Directory.Exists(sourceDir)) return;

                foreach (var dirPath in Directory.GetDirectories(sourceDir, "*", SearchOption.AllDirectories))
                {
                    Directory.CreateDirectory(dirPath.Replace(sourceDir, targetDir));
                }

                foreach (var newPath in Directory.GetFiles(sourceDir, "*.*", SearchOption.AllDirectories))
                {
                    string targetFile = newPath.Replace(sourceDir, targetDir);
                    if (!File.Exists(targetFile))
                    {
                        File.Copy(newPath, targetFile, true);
                    }
                }
            }
            catch
            {
                // Fallback gracefully on copy failures
            }
        }
    }
}
