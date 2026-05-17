using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using MySqlConnector;
using Renci.SshNet;

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

    // ─────────────────────────────────────────────────────────────────────────────
    // SSH-Tunneled variant — forwards local port → remote MySQL through an SSH jump
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Opens an SSH tunnel (via ForwardedPortLocal) so TGToolKit can reach a MySQL
    /// instance that is only accessible on the server's internal network (i.e. blocked
    /// externally on port 3306). Uses the same SSH credentials already stored by the
    /// ServerLinter SFTP config.
    ///
    /// Port resolution order:
    ///   1. preferredLocalPort if it is free
    ///   2. A randomly chosen available port (OS-assigned)
    /// </summary>
    public sealed class SshTunneledDatabaseProvider : IDisposable
    {
        private SshClient?           _sshClient;
        private ForwardedPortLocal?  _tunnel;
        private DatabaseProvider?    _innerProvider;

        public uint ActualLocalPort { get; private set; }

        /// <summary>
        /// Establishes the SSH tunnel and returns a ready-to-use DatabaseProvider
        /// connected through that tunnel.
        /// </summary>
        public DatabaseProvider Connect(
            string sshHost,   int sshPort,  string sshUser, string sshPass,
            string mysqlHost, int mysqlPort,
            string dbUser,    string dbPass, string dbName,
            int preferredLocalPort = 33060)
        {
            _sshClient = new SshClient(sshHost, sshPort, sshUser, sshPass);
            _sshClient.Connect();

            uint localPort = ResolveLocalPort((uint)preferredLocalPort);
            ActualLocalPort = localPort;

            _tunnel = new ForwardedPortLocal(
                "127.0.0.1", localPort,
                mysqlHost,   (uint)mysqlPort);

            _sshClient.AddForwardedPort(_tunnel);
            _tunnel.Start();

            _innerProvider = new DatabaseProvider(
                "127.0.0.1", (int)localPort,
                dbUser, dbPass, dbName);

            return _innerProvider;
        }

        /// <summary>
        /// Scans harvested .sql files from workspaceRoot and executes them through the tunnel.
        /// Automatically injects "IF NOT EXISTS" into CREATE TABLE statements that omit it.
        /// Skips files larger than 1 MB to avoid executing full database dump files.
        /// </summary>
        public async Task<int> ExecuteSetupScriptsAsync(
            string workspaceRoot,
            AuditLogger auditLog)
        {
            if (_innerProvider is null) throw new InvalidOperationException("Call Connect() first.");

            int tablesInjected = 0;

            var sqlFiles = Directory.GetFiles(workspaceRoot, "*.sql", SearchOption.AllDirectories)
                .Where(f => new FileInfo(f).Length <= 1_048_576) // Skip >1 MB dumps
                .ToList();

            if (sqlFiles.Count == 0) return 0;

            foreach (var file in sqlFiles)
            {
                string script = await File.ReadAllTextAsync(file);

                // Guard: inject IF NOT EXISTS where the author forgot it
                script = Regex.Replace(
                    script,
                    @"CREATE TABLE\s+(?!IF\s+NOT\s+EXISTS)([`\w]+)",
                    "CREATE TABLE IF NOT EXISTS $1",
                    RegexOptions.IgnoreCase);

                try
                {
                    await _innerProvider.ExecuteSqlAsync(script);
                    tablesInjected++;
                    auditLog.LogChange(
                        file.Replace(workspaceRoot, string.Empty),
                        "SQL Injected",
                        "Database tables verified/created via SSH Tunnel.");
                }
                catch (Exception ex)
                {
                    auditLog.LogChange(
                        file.Replace(workspaceRoot, string.Empty),
                        "[SQL ERROR]",
                        $"Failed to execute: {ex.Message}");
                }
            }

            return tablesInjected;
        }

        // ── Port resolution ─────────────────────────────────────────────────────

        private static uint ResolveLocalPort(uint preferred)
        {
            if (IsPortAvailable(preferred))
                return preferred;

            // Fall back to an OS-assigned free port
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            uint free = (uint)((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return free;
        }

        private static bool IsPortAvailable(uint port)
        {
            try
            {
                using var l = new TcpListener(IPAddress.Loopback, (int)port);
                l.Start();
                l.Stop();
                return true;
            }
            catch
            {
                return false;
            }
        }

        public void Dispose()
        {
            _tunnel?.Stop();
            _tunnel?.Dispose();
            _sshClient?.Disconnect();
            _sshClient?.Dispose();
        }
    }
}
