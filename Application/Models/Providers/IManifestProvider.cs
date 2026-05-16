using System.Collections.Generic;
using System.Threading.Tasks;

namespace ToolKitV.Models.Providers
{
    /// <summary>
    /// Strategy Pattern abstraction over the file system.
    /// The ServerLinter engine calls this interface exclusively —
    /// it doesn't know or care whether it's reading from disk or SFTP.
    /// </summary>
    public interface IManifestProvider
    {
        /// <summary>
        /// Returns a list of (resourceName, manifestContent) pairs for every
        /// fxmanifest.lua / __resource.lua found under the root path.
        /// </summary>
        Task<List<(string ResourceName, string Content)>> GetManifestsAsync(string rootPath);

        /// <summary>
        /// Returns a map of resourceName → list of stream file basenames.
        /// Used to detect cross-resource stream file conflicts.
        /// </summary>
        Task<Dictionary<string, List<string>>> GetStreamFileMapAsync(string rootPath);
    }
}
