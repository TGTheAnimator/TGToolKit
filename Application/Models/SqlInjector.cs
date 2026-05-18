using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using MySqlConnector;

namespace ToolKitV.Models
{
    public static class SqlInjector
    {
        public static async Task<int> InjectDatabaseTablesAsync(string workspaceRoot, string connectionString, AuditLogger auditLog)
        {
            int tablesInjected = 0;
            
            // Hunt for setup or install SQL files
            var sqlFiles = Directory.GetFiles(workspaceRoot, "*.sql", SearchOption.AllDirectories);
            if (sqlFiles.Length == 0) return 0;

            using var connection = new MySqlConnection(connectionString);
            await connection.OpenAsync();

            foreach (var file in sqlFiles)
            {
                // Skip SQL files that look like backups or massive dumps to be safe
                if (new FileInfo(file).Length > 1024 * 1024) continue; 

                string sqlScript = await File.ReadAllTextAsync(file);
                
                // Add "IF NOT EXISTS" natively if the script author forgot, preventing duplicate table crashes
                sqlScript = Regex.Replace(sqlScript, @"CREATE TABLE\s+(?!IF NOT EXISTS)([`\w]+)", "CREATE TABLE IF NOT EXISTS $1", RegexOptions.IgnoreCase);

                using var command = new MySqlCommand(sqlScript, connection);
                try
                {
                    await command.ExecuteNonQueryAsync();
                    tablesInjected++;
                    auditLog.LogChange(file.Replace(workspaceRoot, ""), "SQL Injected", "Database tables successfully created/verified.");
                }
                catch (MySqlException ex)
                {
                    auditLog.LogChange(file.Replace(workspaceRoot, ""), "[SQL ERROR]", $"Failed to inject tables: {ex.Message}");
                }
            }

            return tablesInjected;
        }
    }
}
