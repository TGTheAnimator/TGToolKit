using CodeWalker.GameFiles;
using CodeWalker.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ToolKitV.Models;

namespace ToolKitV.Models
{
    public static class YtdSplitter
    {
        // FiveM streaming budget: 16 MB virtual is the hard limit before the engine
        // starts dropping textures. We target 14.5 MB to give a comfortable safety margin.
        private const float MaxChunkMb = 14.5f;

        public struct SplitResult
        {
            public int  FilesScanned;
            public int  FilesSafe;
            public int  FilesSplit;
            public int  ChunksGenerated;
            public bool MetaGenerated;
        }

        // ─── Public API ──────────────────────────────────────────────────────────

        public static async Task<SplitResult> SplitDirectoryAsync(
            string          inputDir,
            string          outputDir,
            IProgress<int>? progress,
            LogWriter?      log)
        {
            return await Task.Run(() => SplitDirectory(inputDir, outputDir, progress, log));
        }

        private static SplitResult SplitDirectory(
            string          inputDir,
            string          outputDir,
            IProgress<int>? progress,
            LogWriter?      log)
        {
            SplitResult result = default;
            Directory.CreateDirectory(outputDir);

            string[] files = Directory.GetFiles(inputDir, "*.ytd", SearchOption.AllDirectories);
            if (files.Length == 0) return result;

            // Tracks the original filename → list of chunk filenames for meta generation
            var txdRelationships = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < files.Length; i++)
            {
                string filePath = files[i];
                string fileName = Path.GetFileName(filePath);
                string baseName = Path.GetFileNameWithoutExtension(fileName);

                result.FilesScanned++;
                log?.LogWrite($"[SCAN] {fileName}");

                // Check virtual size via RSC7 header
                var (virtualMb, _) = Rsc7SizeHelper.GetFileSize(filePath);

                if (virtualMb <= MaxChunkMb || virtualMb == 0f)
                {
                    // Safe — copy straight to output
                    string dest = Path.Combine(outputDir, fileName);
                    File.Copy(filePath, dest, overwrite: true);
                    result.FilesSafe++;
                    log?.LogWrite($"  Safe ({virtualMb:F2} MB) — copied.");
                }
                else
                {
                    log?.LogWrite($"  Oversized ({virtualMb:F2} MB) — splitting…");
                    try
                    {
                        var chunkNames = SplitYtd(filePath, outputDir, baseName, log);
                        if (chunkNames.Count > 1)
                        {
                            txdRelationships[baseName] = chunkNames;
                            result.FilesSplit++;
                            result.ChunksGenerated += chunkNames.Count;
                        }
                        else
                        {
                            // Only one chunk produced — file was not actually splittable
                            result.FilesSafe++;
                        }
                    }
                    catch (Exception ex)
                    {
                        log?.LogWrite($"  ERROR: {ex.Message}");
                        // Copy original as fallback
                        File.Copy(filePath, Path.Combine(outputDir, fileName), overwrite: true);
                        result.FilesSafe++;
                    }
                }

                progress?.Report((i + 1) * 100 / files.Length);
            }

            // Generate TXD relationship meta if any files were split
            if (txdRelationships.Count > 0)
            {
                GenerateTxdMeta(txdRelationships, outputDir, log);
                result.MetaGenerated = true;
            }

            return result;
        }

        // ─── Core split logic ────────────────────────────────────────────────────

        private static List<string> SplitYtd(
            string     filePath,
            string     outputDir,
            string     baseName,
            LogWriter? log)
        {
            // Load the YTD
            byte[]       data = File.ReadAllBytes(filePath);
            string       name = Path.GetFileName(filePath);
            RpfFileEntry fe   = CreateFileEntry(name, filePath, ref data);
            YtdFile      ytd  = RpfFile.GetFile<YtdFile>(fe, data);

            if (ytd?.TextureDict?.Textures == null || ytd.TextureDict.Textures.Count == 0)
                return new List<string> { name };

            // Collect all textures with their individual virtual sizes
            var textures = ytd.TextureDict.Textures.data_items.ToList();

            // First Fit Descending bin-packing — each texture's size is estimated individually
            var chunks = BinPack(textures, 0f); // perTexMb param unused — EstimateTextureVirtualMB is called per-item

            log?.LogWrite($"  {textures.Count} textures → {chunks.Count} chunk(s)");

            var chunkNames = new List<string>();

            for (int i = 0; i < chunks.Count; i++)
            {
                // Chunk 0 keeps the original name so model references still resolve
                string chunkName = i == 0 ? $"{baseName}.ytd" : $"{baseName}_{i}.ytd";
                string chunkPath = Path.Combine(outputDir, chunkName);

                SaveYtdChunk(chunks[i], chunkPath);
                chunkNames.Add(Path.GetFileNameWithoutExtension(chunkName));
                log?.LogWrite($"  → {chunkName} ({chunks[i].Count} textures)");
            }

            return chunkNames;
        }

        private static List<List<Texture>> BinPack(List<Texture> textures, float perTexMb)
        {
            // Sort largest-first for better bin utilisation
            var sorted = textures.OrderByDescending(t => EstimateTextureVirtualMB(t)).ToList();
            var chunks = new List<List<Texture>>();
            var sizes  = new List<float>();

            foreach (var tex in sorted)
            {
                float texSize = EstimateTextureVirtualMB(tex);

                bool placed = false;
                for (int b = 0; b < chunks.Count; b++)
                {
                    if (sizes[b] + texSize <= MaxChunkMb)
                    {
                        chunks[b].Add(tex);
                        sizes[b] += texSize;
                        placed = true;
                        break;
                    }
                }

                if (!placed)
                {
                    chunks.Add(new List<Texture> { tex });
                    sizes.Add(texSize);
                }
            }

            return chunks;
        }

        /// <summary>
        /// Mathematically estimates how much VRAM a texture consumes in FiveM.
        /// Accounts for block compression (BC1/BC3/BC7) and mipmap chains (×1.33).
        /// From the original YTD Splitter specification.
        /// </summary>
        private static float EstimateTextureVirtualMB(Texture tex)
        {
            float bytesPerPixel = 1.0f; // Default for DXT5/BC3/BC7 (8 bits per block)

            if (tex.Format == TextureFormat.D3DFMT_DXT1 || tex.Format == TextureFormat.D3DFMT_ATI1)
                bytesPerPixel = 0.5f; // BC1/BC4 use half the space (4 bits per block)
            else if (tex.Format == TextureFormat.D3DFMT_A8R8G8B8 || tex.Format == TextureFormat.D3DFMT_A8B8G8R8)
                bytesPerPixel = 4.0f; // Uncompressed 32-bit RGBA

            // Width × Height × BPP × 1.33 (mipmap chain overhead)
            float estimatedBytes = tex.Width * tex.Height * bytesPerPixel * 1.33f;
            return estimatedBytes / 1024f / 1024f;
        }

        private static void SaveYtdChunk(List<Texture> textures, string outputPath)
        {
            var newYtd = new YtdFile();
            newYtd.TextureDict = new TextureDictionary();
            newYtd.TextureDict.Textures = new ResourcePointerList64<Texture>();
            newYtd.TextureDict.BuildFromTextureList(textures);

            byte[] saved = newYtd.Save();
            File.WriteAllBytes(outputPath, saved);
        }

        // ─── TXD relationship meta generation ───────────────────────────────────

        private static void GenerateTxdMeta(
            Dictionary<string, List<string>> relationships,
            string                            outputDir,
            LogWriter?                        log)
        {
            var sb = new StringBuilder();
            sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
            sb.AppendLine("<CVehicleModelInfo__InitDataList>");
            sb.AppendLine("  <residentTxd>vehshare</residentTxd>");
            sb.AppendLine("  <residentAnims />");
            sb.AppendLine("  <InitDatas />");
            sb.AppendLine("  <txdRelationships>");

            foreach (var (parentName, children) in relationships)
            {
                // The parent dict (chunk 0) keeps the original name.
                // Each extra chunk registers as a child so the engine falls back to the parent.
                foreach (var child in children.Skip(1))
                {
                    sb.AppendLine("    <Item>");
                    sb.AppendLine($"      <parent>{parentName}</parent>");
                    sb.AppendLine($"      <child>{child}</child>");
                    sb.AppendLine("    </Item>");
                }
            }

            sb.AppendLine("  </txdRelationships>");
            sb.AppendLine("</CVehicleModelInfo__InitDataList>");

            string metaPath = Path.Combine(outputDir, "split_txd_relationships.meta");
            File.WriteAllText(metaPath, sb.ToString(), Encoding.UTF8);

            // fxmanifest snippet — drag-and-drop ready
            string fxSnippet =
                "files {\n" +
                "    'split_txd_relationships.meta'\n" +
                "}\n" +
                "data_file 'VEHICLE_METADATA_FILE' 'split_txd_relationships.meta'";
            File.WriteAllText(Path.Combine(outputDir, "fxmanifest_snippet.lua"), fxSnippet, Encoding.UTF8);

            log?.LogWrite($"[META] Generated split_txd_relationships.meta ({relationships.Count} parent dict(s))");
        }

        // ─── CodeWalker helpers (same pattern as TextureOptimization.cs) ─────────

        private static RpfFileEntry CreateFileEntry(string name, string path, ref byte[] data)
        {
            uint rsc7 = (data?.Length > 4) ? BitConverter.ToUInt32(data, 0) : 0;

            RpfFileEntry e;
            if (rsc7 == 0x37435352) // RSC7 magic
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
    }
}
