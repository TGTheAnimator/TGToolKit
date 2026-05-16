using CodeWalker.GameFiles;
using CodeWalker.Utils;
using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using ToolKitV.Models;

namespace ToolkitV.Models
{
    public partial class TextureOptimization
    {
        // ─── Public data structures ──────────────────────────────────────────────

        public struct StatsData
        {
            public int   filesCount;
            public int   oversizedCount;
            public float virtualSize;
            public float physicalSize;

            public StatsData()
            {
                filesCount     = 0;
                oversizedCount = 0;
                virtualSize    = 0;
                physicalSize   = 0;
            }
        }

        public struct ResultsData
        {
            public float filesSize;
            public int   filesOptimized;
            public float optimizedSize;
            public float optimizedProcent;

            public ResultsData()
            {
                filesSize        = 0;
                filesOptimized   = 0;
                optimizedSize    = 0;
                optimizedProcent = 0;
            }
        }

        /// <summary>
        /// Result data returned by <see cref="FixScriptRTs"/>.
        /// </summary>
        public struct ScriptRtResultsData
        {
            public int ytdsScanned;
            public int ytdsFixed;
            public int texturesFixed;

            public ScriptRtResultsData()
            {
                ytdsScanned  = 0;
                ytdsFixed    = 0;
                texturesFixed = 0;
            }
        }

        // ─── Temporary DDS file helpers ──────────────────────────────────────────

        /// <summary>
        /// Exports a Texture to a uniquely-named temp .dds file.
        /// Caller is responsible for deleting the file when done.
        /// Returns null on failure.
        /// </summary>
        private static string? CreateTempTextureFile(Texture texture)
        {
            // Use a system temp file so we never collide and always have write access.
            string tempPath = Path.GetTempFileName();
            // GetTempFileName creates a .tmp file — rename extension to .dds so texconv accepts it.
            string ddsPath = Path.ChangeExtension(tempPath, ".dds");
            File.Move(tempPath, ddsPath);

            try
            {
                byte[] dds = DDSIO.GetDDSFile(texture);
                File.WriteAllBytes(ddsPath, dds);
                return ddsPath;
            }
            catch
            {
                // Clean up on failure.
                SafeDelete(ddsPath);
                return null;
            }
        }

        private static void SafeDelete(string? path)
        {
            if (path != null && File.Exists(path))
            {
                try { File.Delete(path); } catch { /* best-effort */ }
            }
        }

        // ─── texconv invocation ──────────────────────────────────────────────────

        /// <summary>
        /// Calls texconv.exe to re-encode the texture to the given DXGI format.
        /// Writes result back into the same temp file path and returns the updated Texture.
        /// </summary>
        private static Texture ConvertTexture(Texture texture, string convertFormat, string tempDdsPath)
        {
            string workingDir = Path.GetDirectoryName(tempDdsPath)!;
            string fileName   = Path.GetFileName(tempDdsPath);

            // Resolve texconv.exe relative to the application binary (not CWD).
            string appDir   = AppDomain.CurrentDomain.BaseDirectory;
            string texConv  = Path.Combine(appDir, "Dependencies", "texconv.exe");

            // -w / -h / -m keep the width, height, and mip-level count from the source texture.
            // -bc d uses the DirectCompute GPU path for faster and higher-quality encoding.
            // -y overwrites the output without prompting.
            // -o sends output to the same directory as the input.
            string args = $"-w {texture.Width} -h {texture.Height} -m {texture.Levels} -f {convertFormat} -bc d \"{fileName}\" -y -o \"{workingDir}\"";

            using Process proc = new();
            proc.StartInfo.FileName               = texConv;
            proc.StartInfo.Arguments              = args;
            proc.StartInfo.WorkingDirectory       = workingDir;
            proc.StartInfo.UseShellExecute        = false;
            proc.StartInfo.CreateNoWindow         = true;
            proc.StartInfo.RedirectStandardOutput = true;
            proc.StartInfo.RedirectStandardError  = true;
            proc.Start();
            proc.WaitForExit();

            // texconv writes <filename>.dds in the output dir — same path we gave it.
            byte[]  newDds = File.ReadAllBytes(tempDdsPath);
            Texture tex    = DDSIO.GetTexture(newDds);

            texture.Data   = tex.Data;
            texture.Depth  = tex.Depth;
            texture.Levels = tex.Levels;
            texture.Format = tex.Format;
            texture.Stride = tex.Stride;

            return texture;
        }

        // ─── FiveM-optimised texture format selection ────────────────────────────

        /// <summary>
        /// Selects the best DXGI format for FiveM/GTA V streaming performance.
        ///
        /// Priority rules (most relevant for FiveM assets):
        ///   • BC7_UNORM  — best quality block compression for RGBA / DXT5 sources.
        ///                  Preferred over BC3 when GPU supports it (all modern hardware).
        ///   • BC1_UNORM  — opaque RGB or 1-bit alpha (DXT1). Half the size of BC3.
        ///   • BC4_UNORM  — single-channel (grayscale, heightmaps, alpha-only textures).
        ///   • BC5_UNORM  — dual-channel normals (XY), let GTA V reconstruct Z.
        ///   • BC3_UNORM  — legacy DXT5 pass-through when format optimisation is OFF.
        ///   • Uncompressed formats preserved as-is when no compression is applicable.
        ///
        /// When formatOptimization=true every texture is forced into the smallest
        /// applicable block-compressed format.
        /// </summary>
        private static string PickTexConvFormat(Texture texture, bool formatOptimization)
        {
            if (formatOptimization)
            {
                // Alpha-carrying formats → BC7 for maximum quality at same size as BC3.
                if (texture.Format is
                    TextureFormat.D3DFMT_DXT5 or
                    TextureFormat.D3DFMT_A1R5G5B5 or
                    TextureFormat.D3DFMT_A8B8G8R8 or
                    TextureFormat.D3DFMT_A8R8G8B8)
                {
                    return "BC7_UNORM";
                }

                // Single-channel sources.
                if (texture.Format is TextureFormat.D3DFMT_A8 or TextureFormat.D3DFMT_L8)
                    return "BC4_UNORM";

                // Dual-channel (ATI2 / normal maps).
                if (texture.Format is TextureFormat.D3DFMT_ATI2)
                    return "BC5_UNORM";

                // Everything else → opaque BC1.
                return "BC1_UNORM";
            }

            // No format optimisation — keep the same compression family, fix BC7 mapping.
            return texture.Format switch
            {
                // Block-compressed — preserve family.
                TextureFormat.D3DFMT_DXT1 => "BC1_UNORM",
                TextureFormat.D3DFMT_DXT3 => "BC2_UNORM",
                TextureFormat.D3DFMT_DXT5 => "BC3_UNORM",
                TextureFormat.D3DFMT_ATI1 => "BC4_UNORM",
                TextureFormat.D3DFMT_ATI2 => "BC5_UNORM",
                TextureFormat.D3DFMT_BC7  => "BC7_UNORM",   // FIX: was incorrectly BC5_UNORM

                // Uncompressed — preserve as-is.
                TextureFormat.D3DFMT_A1R5G5B5 => "B5G5R5A1_UNORM",
                TextureFormat.D3DFMT_A8        => "A8_UNORM",
                TextureFormat.D3DFMT_A8B8G8R8  => "R8G8B8A8_UNORM",
                TextureFormat.D3DFMT_L8        => "R8_UNORM",
                TextureFormat.D3DFMT_A8R8G8B8  => "B8G8R8A8_UNORM",

                // Unknown — fall back to BC1 (safe default).
                _ => "BC1_UNORM",
            };
        }

        // ─── Mip-level validation ────────────────────────────────────────────────

        /// <summary>
        /// GTA V requires a full mip chain for streamed textures.
        /// A full chain for a texture of size N has floor(log2(min(W,H))) + 1 levels,
        /// but texconv caps at the number that reaches 1×1.  We target maxLevel-1 to
        /// leave the last 1×1 mip out (same convention the original code used).
        /// </summary>
        private static byte ClampMipLevels(Texture texture)
        {
            int minSide  = Math.Min(texture.Width, texture.Height);
            int maxLevel = (int)Math.Log2(minSide);   // e.g. 2048 → 11
            // Clamp: never go above what's valid, never drop below 1.
            return (byte)Math.Max(1, Math.Min(texture.Levels, maxLevel - 1));
        }

        // ─── Single-texture optimisation ────────────────────────────────────────

        private static Texture OptimizeTexture(Texture texture, bool formatOptimization, bool downsize, bool autoDownscale4K)
        {
            // Clamp mip levels to the valid maximum for this texture's dimensions.
            texture.Levels = ClampMipLevels(texture);

            string? tempPath = CreateTempTextureFile(texture);
            if (tempPath is null) return texture;

            try
            {
                string format = PickTexConvFormat(texture, formatOptimization);

                if (downsize)
                {
                    // Halve dimensions but guard against going below 4px (BC block minimum).
                    texture.Width  = (ushort)Math.Max(4, texture.Width  / 2);
                    texture.Height = (ushort)Math.Max(4, texture.Height / 2);
                    texture.Levels = ClampMipLevels(texture);
                }
                else if (autoDownscale4K)
                {
                    // Reduce anything > 2048 to a max of 2048
                    while (texture.Width > 2048 || texture.Height > 2048)
                    {
                        texture.Width  = (ushort)Math.Max(4, texture.Width  / 2);
                        texture.Height = (ushort)Math.Max(4, texture.Height / 2);
                    }
                    texture.Levels = ClampMipLevels(texture);
                }

                texture = ConvertTexture(texture, format, tempPath);
            }
            finally
            {
                SafeDelete(tempPath);
            }

            return texture;
        }

        /// <summary>
        /// Script render-target textures must be uncompressed (B8G8R8A8 = D3DFMT_A8R8G8B8).
        /// GTA V will fatal-crash if they are block-compressed (DXT/ATI/BC formats).
        ///
        /// IMPORTANT: the correct DXGI format is B8G8R8A8_UNORM — this maps to D3D9's
        /// A8R8G8B8 byte order. R8G8B8A8_UNORM would produce colour-channel swapping.
        /// </summary>
        private static Texture UncompressScriptTexture(Texture texture)
        {
            string? tempPath = CreateTempTextureFile(texture);
            if (tempPath is null) return texture;

            try
            {
                // B8G8R8A8_UNORM = D3DFMT_A8R8G8B8 — what GTA V expects for script render targets.
                texture = ConvertTexture(texture, "B8G8R8A8_UNORM", tempPath);
            }
            finally
            {
                SafeDelete(tempPath);
            }

            return texture;
        }

        // ─── RSC7 size helpers ───────────────────────────────────────────────────

        /// <summary>
        /// Returns [virtualSizeMB, diskSizeMB] for a .ytd file.
        ///
        /// virtualMB  — decoded from the RSC7 header (VRAM usage).
        ///              This is what FiveM uses for the streaming budget check.
        ///              Return value 0 means the file has no RSC7 header (skip it).
        ///
        /// diskMB     — actual compressed bytes on disk (FileInfo.Length).
        ///              This matches what Windows Explorer shows and is what
        ///              users see in folder Properties.
        /// </summary>
        private static (float virtualMB, float diskMB) GetFileSize(string filePath, LogWriter? logWriter)
        {
            var (vMB, diskMB) = Rsc7SizeHelper.GetFileSize(filePath);
            
            if (vMB > 0f)
                logWriter?.LogWrite($"[RSC7] {filePath} | virtual={vMB:F2} MB, disk={diskMB:F2} MB");
            else if (diskMB == 0f)
                logWriter?.LogWrite($"[ERROR] Could not read {filePath}");
                
            return (vMB, diskMB);
        }

        // ─── YTD loading helpers ─────────────────────────────────────────────────

        private static RpfFileEntry CreateFileEntry(string name, string path, ref byte[] data)
        {
            uint rsc7 = (data?.Length > 4) ? BitConverter.ToUInt32(data, 0) : 0;

            RpfFileEntry e;
            if (rsc7 == 0x37435352) // RSC7 header
            {
                e    = RpfFile.CreateResourceFileEntry(ref data, 0);
                data = ResourceBuilder.Decompress(data);
            }
            else
            {
                RpfBinaryFileEntry be = new()
                {
                    FileSize = (uint)(data?.Length ?? 0)
                };
                be.FileUncompressedSize = be.FileSize;
                e = be;
            }

            e.Name          = name;
            e.NameLower     = name?.ToLowerInvariant();
            e.NameHash      = JenkHash.GenHash(e.NameLower);
            e.ShortNameHash = JenkHash.GenHash(Path.GetFileNameWithoutExtension(e.NameLower));
            e.Path          = path;
            return e;
        }

        private static YtdFile CreateYtdFile(string path)
        {
            byte[]       data = File.ReadAllBytes(path);
            string       name = new FileInfo(path).Name;
            RpfFileEntry fe   = CreateFileEntry(name, path, ref data);
            return RpfFile.GetFile<YtdFile>(fe, data);
        }

        // ─── Public API — Optimize ───────────────────────────────────────────────

        /// <summary>
        /// Optimises all .ytd files found recursively under <paramref name="inputDirectory"/>.
        ///
        /// FiveM streaming budget is based on virtual (GPU) size, so oversized detection
        /// uses virtualMB — not physicalMB as the original code did.
        ///
        /// <paramref name="optimizeSize"/> is the threshold (Width + Height in px) above
        /// which individual textures are reprocessed.
        /// </summary>
        public static ResultsData Optimize(
            string   inputDirectory,
            string   backupDirectory,
            string   optimizeSize,
            bool     onlyOverSized,
            bool     downsize,
            bool     formatOptimization,
            bool     autoDownscale4K,
            Delegate optimizeProgressHandler)
        {
            ResultsData results = new();

            string[] inputFiles       = Directory.GetFiles(inputDirectory, "*.ytd", SearchOption.AllDirectories);
            ushort   optimizeSizeVal  = Convert.ToUInt16(optimizeSize);
            bool     doBackup         = !string.IsNullOrEmpty(backupDirectory);
            int      currentProgress  = 0;

            LogWriter log = new("=== TGToolKit optimisation started ===");

            for (int i = 0; i < inputFiles.Length; i++)
            {
                string filePath = inputFiles[i];
                string fileName = Path.GetFileName(filePath);

                log.LogWrite($"Processing: {filePath}");

                (float virtualMB, float diskMB) = GetFileSize(filePath, log);

                // virtualMB == 0 means no RSC7 header — not a valid GTA V resource, skip entirely.
                bool noRsc7 = virtualMB == 0f;

                // FIX: use virtualMB for the FiveM streaming budget check (was physicalMB).
                if (onlyOverSized && (noRsc7 || virtualMB < 16f))
                {
                    log.LogWrite($"  Skipped (not oversized): virtual={virtualMB:F2} MB");
                    continue;
                }

                if (noRsc7)
                {
                    log.LogWrite($"  Skipped (no RSC7 header / invalid file)");
                    continue;
                }

                YtdFile ytdFile;
                try
                {
                    ytdFile = CreateYtdFile(filePath);
                }
                catch (Exception ex)
                {
                    log.LogWrite($"  ERROR loading YTD: {ex.Message}");
                    continue;
                }

                bool ytdChanged = false;

                for (int j = 0; j < ytdFile.TextureDict.Textures.Count; j++)
                {
                    Texture texture          = ytdFile.TextureDict.Textures[j];
                    bool    isScriptTexture  = texture.Name.Contains("script_rt", StringComparison.OrdinalIgnoreCase);

                    // Script render-targets must stay uncompressed — decompress if needed.
                    if (isScriptTexture && IsTextureCompressed(texture))
                    {
                        log.LogWrite($"  Decompressing script RT: {texture.Name}");
                        ytdFile.TextureDict.Textures.data_items[j] = UncompressScriptTexture(texture);
                        results.filesOptimized++;
                        ytdChanged = true;
                        continue;
                    }

                    // Normal textures — optimise if Width+Height meets the threshold.
                    if (!isScriptTexture && (texture.Width + texture.Height) >= optimizeSizeVal)
                    {
                        // Create backup BEFORE the first write to this file.
                        if (!ytdChanged && doBackup)
                        {
                            BackupFile(filePath, fileName, inputDirectory, backupDirectory, log);
                        }

                        // FIX: ytdChanged = true is now ALWAYS set here, not only inside doBackup.
                        ytdChanged = true;

                        log.LogWrite($"  Optimising texture: {texture.Name} ({texture.Width}x{texture.Height}, {texture.Format})");
                        ytdFile.TextureDict.Textures.data_items[j] = OptimizeTexture(texture, formatOptimization, downsize, autoDownscale4K);
                        results.filesOptimized++;
                    }
                }

                if (ytdChanged)
                {
                    byte[] newData   = ytdFile.Save();
                    File.WriteAllBytes(filePath, newData);

                    (float newVirtMB, float newDiskMB) = GetFileSize(filePath, log);
                    results.optimizedSize += diskMB - newDiskMB;

                    log.LogWrite($"  Saved. Disk: {diskMB:F2} MB → {newDiskMB:F2} MB (saved {diskMB - newDiskMB:F2} MB)");
                }

                int progress = i * 100 / inputFiles.Length;
                if (currentProgress != progress)
                {
                    optimizeProgressHandler?.DynamicInvoke(results, progress);
                    currentProgress = progress;
                }
            }

            optimizeProgressHandler?.DynamicInvoke(results, 100);
            log.LogWrite("=== TGToolKit optimisation finished ===");
            return results;
        }

        // ─── Public API — Fix Script RT Crashes ──────────────────────────────────

        /// <summary>
        /// Scans every .ytd under <paramref name="inputDirectory"/> (all subfolders)
        /// and decompresses any <c>script_rt_*</c> texture that is block-compressed.
        ///
        /// This is the targeted fix for fatal GTA V crashes caused by textures such as
        /// <c>script_rt_dials_itali</c> inside <c>gsts121.ytd</c>.
        ///
        /// The operation is completely independent of the Optimize settings — it always
        /// processes every YTD regardless of file size or the onlyOverSized toggle.
        /// </summary>
        public static ScriptRtResultsData FixScriptRTs(
            string   inputDirectory,
            string   backupDirectory,
            Delegate progressHandler)
        {
            ScriptRtResultsData results  = new();
            string[] inputFiles          = Directory.GetFiles(inputDirectory, "*.ytd", SearchOption.AllDirectories);
            bool     doBackup            = !string.IsNullOrEmpty(backupDirectory);
            int      currentProgress     = 0;

            LogWriter log = new("=== TGToolKit Script RT Fix started ===");

            for (int i = 0; i < inputFiles.Length; i++)
            {
                string filePath = inputFiles[i];
                string fileName = Path.GetFileName(filePath);

                results.ytdsScanned++;

                YtdFile ytdFile;
                try
                {
                    ytdFile = CreateYtdFile(filePath);
                }
                catch (Exception ex)
                {
                    log.LogWrite($"  ERROR loading {fileName}: {ex.Message}");
                    goto Progress;
                }

                bool ytdChanged = false;

                for (int j = 0; j < ytdFile.TextureDict.Textures.Count; j++)
                {
                    Texture texture = ytdFile.TextureDict.Textures[j];

                    bool isScriptRt = texture.Name.Contains("script_rt", StringComparison.OrdinalIgnoreCase);
                    if (!isScriptRt || !IsTextureCompressed(texture))
                        continue;

                    log.LogWrite($"[FIX] {fileName} → decompressing script RT '{texture.Name}' ({texture.Format})");

                    if (!ytdChanged && doBackup)
                        BackupFile(filePath, fileName, inputDirectory, backupDirectory, log);

                    ytdFile.TextureDict.Textures.data_items[j] = UncompressScriptTexture(texture);
                    results.texturesFixed++;
                    ytdChanged = true;
                }

                if (ytdChanged)
                {
                    byte[] newData = ytdFile.Save();
                    File.WriteAllBytes(filePath, newData);
                    results.ytdsFixed++;
                    log.LogWrite($"  Saved {fileName} with {results.texturesFixed} script RT(s) decompressed.");
                }

                Progress:
                int progress = i * 100 / inputFiles.Length;
                if (currentProgress != progress)
                {
                    progressHandler?.DynamicInvoke(results, progress);
                    currentProgress = progress;
                }
            }

            progressHandler?.DynamicInvoke(results, 100);
            log.LogWrite("=== TGToolKit Script RT Fix finished ===");
            return results;
        }

        // ─── Public API — Analyse ────────────────────────────────────────────────

        /// <summary>
        /// Scans all .ytd files and returns aggregate stats.
        /// Oversized threshold is based on virtual size (GPU budget) per FiveM conventions.
        /// </summary>
        public static StatsData GetStatsData(string path, Delegate? updateHandler)
        {
            StatsData results = new();
            string[] inputFiles = Directory.GetFiles(path, "*.ytd", SearchOption.AllDirectories);

            if (inputFiles.Length == 0)
                return results;

            results.filesCount = inputFiles.Length;
            int currentProgress = 0;

            for (int i = 0; i < inputFiles.Length; i++)
            {
                (float virtualMB, float physicalMB) = GetFileSize(inputFiles[i], null);

                results.virtualSize  += virtualMB;
                results.physicalSize += physicalMB;

                // FIX: use virtual size for FiveM streaming budget oversized check.
                if (virtualMB > 16f)
                    results.oversizedCount++;

                int progress = i * 100 / inputFiles.Length;
                if (currentProgress != progress)
                {
                    updateHandler?.DynamicInvoke(progress);
                    currentProgress = progress;
                }
            }

            updateHandler?.DynamicInvoke(100);
            return results;
        }

        // ─── Private helpers ─────────────────────────────────────────────────────

        private static bool IsTextureCompressed(Texture texture)
        {
            // Match all DXT/ATI/BC block-compressed formats.
            return Regex.IsMatch(texture.Format.ToString(), @"D3DFMT_(DXT|ATI|BC)\d");
        }

        private static void BackupFile(
            string filePath,
            string fileName,
            string inputDirectory,
            string backupDirectory,
            LogWriter log)
        {
            try
            {
                string relativePath = Path.GetRelativePath(inputDirectory, filePath);
                string destDir      = Path.GetDirectoryName(Path.Combine(backupDirectory, relativePath))!;

                Directory.CreateDirectory(destDir);

                string destPath = Path.Combine(destDir, fileName);
                File.Copy(filePath, destPath, overwrite: true);
                log.LogWrite($"  Backed up to: {destPath}");
            }
            catch (Exception ex)
            {
                log.LogWrite($"  WARNING: backup failed: {ex.Message}");
            }
        }
    }
}