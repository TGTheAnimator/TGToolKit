using System.Collections.Generic;
using System.Threading.Tasks;

namespace ToolKitV.Models.Providers
{
    /// <summary>
    /// Abstracts all filesystem I/O for the Auto-Wirer so it operates identically
    /// over a local drive or a remote SFTP (RocketNode / Pterodactyl) connection.
    /// </summary>
    public interface IFileSystemProvider
    {
        // ── Traversal ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Returns full paths of all files under <paramref name="rootPath"/> whose
        /// names match <paramref name="searchPattern"/> (e.g. <c>"fxmanifest.lua"</c>
        /// or <c>"*.lua"</c>).  Searches recursively.
        /// </summary>
        Task<List<string>> DiscoverFilesAsync(string rootPath, string searchPattern);

        // ── I/O ────────────────────────────────────────────────────────────────────

        /// <summary>Reads the full text content of a file at <paramref name="path"/>.</summary>
        Task<string> ReadAllTextAsync(string path);

        /// <summary>Writes <paramref name="content"/> to <paramref name="path"/>, overwriting existing data.</summary>
        Task WriteAllTextAsync(string path, string content);

        // ── Safety ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Creates <c>{originalPath}.tg_backup</c> if it does not already exist.
        /// Silently skips when a backup is already present (idempotent).
        /// </summary>
        Task CreateBackupAsync(string originalPath);

        /// <summary>Renames/moves a directory from <paramref name="oldPath"/> to <paramref name="newPath"/>.</summary>
        Task RenameDirectoryAsync(string oldPath, string newPath);

        /// <summary>Frees any underlying connection or stream resources.</summary>
        void Disconnect();
    }
}
