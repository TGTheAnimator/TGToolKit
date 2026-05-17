using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MySqlConnector;

namespace ToolKitV.Models.Providers
{
    /// <summary>
    /// Provides low-latency, fully asynchronous database operations using the MySqlConnector driver.
    /// Safely handles metadata scans and multi-statement transactional executions.
    /// </summary>
    public class DatabaseProvider
    {
        private readonly string _connectionString;

        public DatabaseProvider(string host, int port, string user, string password, string database)
        {
            // Build a highly optimized connection string with reasonable timeouts so it doesn't freeze the app
            var builder = new MySqlConnectionStringBuilder
            {
                Server = host,
                Port = (uint)port,
                UserID = user,
                Password = password,
                Database = database,
                ConnectionTimeout = 10, // 10 seconds timeout for fast feedback
                AllowUserVariables = true
            };
            _connectionString = builder.ConnectionString;
        }

        /// <summary>
        /// Tests if a connection can be established successfully.
        /// Throws exceptions with descriptive error details on failure.
        /// </summary>
        public async Task TestConnectionAsync()
        {
            using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();
        }

        /// <summary>
        /// Retrieves the list of all table names existing in the active database.
        /// </summary>
        public async Task<HashSet<string>> GetLiveTablesAsync()
        {
            var tables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();

            string query = "SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = DATABASE();";
            using var command = new MySqlCommand(query, connection);
            using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                if (!reader.IsDBNull(0))
                {
                    tables.Add(reader.GetString(0));
                }
            }

            return tables;
        }

        /// <summary>
        /// Retrieves the list of columns inside a specific table.
        /// </summary>
        public async Task<HashSet<string>> GetLiveColumnsAsync(string tableName)
        {
            var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();

            string query = "SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @tableName;";
            using var command = new MySqlCommand(query, connection);
            command.Parameters.AddWithValue("@tableName", tableName);

            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                if (!reader.IsDBNull(0))
                {
                    columns.Add(reader.GetString(0));
                }
            }

            return columns;
        }

        /// <summary>
        /// Executes raw SQL schema/DDL modifications inside the active database.
        /// </summary>
        public async Task ExecuteSqlAsync(string rawSql)
        {
            if (string.IsNullOrWhiteSpace(rawSql)) return;

            using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();

            using var transaction = await connection.BeginTransactionAsync();
            try
            {
                using var command = new MySqlCommand(rawSql, connection, transaction);
                await command.ExecuteNonQueryAsync();
                await transaction.CommitAsync();
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                throw new InvalidOperationException($"SQL Execution failed:\n\n{ex.Message}", ex);
            }
        }
    }
}
