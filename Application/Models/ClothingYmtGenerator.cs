using CodeWalker.GameFiles;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;

namespace ToolKitV.Models
{
    public static class ClothingYmtGenerator
    {
        public struct GenerationResult
        {
            public int DrawablesFound;
            public int TexturesFound;
            public bool Success;
            public string ErrorMessage;
            public string OutputPath;
        }

        private static readonly HashSet<string> PropPrefixes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "p_head", "p_eyes", "p_ears", "p_mouth", "p_lhand", "p_rhand", "p_lwrist", "p_rwrist", "p_hip", "p_lfoot", "p_rfoot", "p_ph_l_hand", "p_ph_r_hand", "p_cs_ph_l_hand", "p_cs_ph_r_hand"
        };

        public static GenerationResult GeneratePack(string inputFolder, string packName, string pedTarget)
        {
            var result = new GenerationResult();
            if (string.IsNullOrWhiteSpace(inputFolder) || !Directory.Exists(inputFolder))
            {
                result.ErrorMessage = "Input folder does not exist.";
                return result;
            }

            if (string.IsNullOrWhiteSpace(packName)) packName = "custom_clothing";
            if (string.IsNullOrWhiteSpace(pedTarget)) pedTarget = "mp_m_freemode_01";

            // Find all files
            var yddFiles = Directory.GetFiles(inputFolder, "*.ydd", SearchOption.TopDirectoryOnly)
                                    .Select(f => Path.GetFileNameWithoutExtension(f).ToLowerInvariant())
                                    .ToList();
            
            var ytdFiles = Directory.GetFiles(inputFolder, "*.ytd", SearchOption.TopDirectoryOnly)
                                    .Select(f => Path.GetFileNameWithoutExtension(f).ToLowerInvariant())
                                    .ToList();

            result.DrawablesFound = yddFiles.Count;
            result.TexturesFound = ytdFiles.Count;

            if (yddFiles.Count == 0)
            {
                result.ErrorMessage = "No .ydd (drawable) files found in the folder.";
                return result;
            }

            // Separate components and props
            var compDrawables = new List<string>();
            var propDrawables = new List<string>();

            foreach (var ydd in yddFiles)
            {
                bool isProp = PropPrefixes.Any(p => ydd.StartsWith(p + "_", StringComparison.OrdinalIgnoreCase) || ydd.Contains("^" + p + "_", StringComparison.OrdinalIgnoreCase));
                if (isProp) propDrawables.Add(ydd);
                else compDrawables.Add(ydd);
            }

            // Remove ped prefixes if present (e.g. mp_m_freemode_01^jbib_000_u)
            string StripPrefix(string name)
            {
                int idx = name.IndexOf('^');
                if (idx >= 0) return name.Substring(idx + 1);
                return name;
            }

            try
            {
                string outputDir = Path.Combine(inputFolder, "stream");
                Directory.CreateDirectory(outputDir);

                // Move/copy all files into the stream folder so it's a ready-to-use resource
                foreach (var file in Directory.GetFiles(inputFolder))
                {
                    string ext = Path.GetExtension(file).ToLowerInvariant();
                    if (ext == ".ydd" || ext == ".ytd")
                    {
                        string dest = Path.Combine(outputDir, Path.GetFileName(file));
                        if (file != dest) File.Copy(file, dest, true);
                    }
                }

                // Generate Components YMT
                if (compDrawables.Count > 0)
                {
                    string xml = GenerateComponentXml(compDrawables, ytdFiles, StripPrefix);
                    byte[] ymtData = CompileXmlToYmt(xml);
                    File.WriteAllBytes(Path.Combine(inputFolder, $"{packName}_clothes.ymt"), ymtData);
                }

                // Generate Props YMT
                if (propDrawables.Count > 0)
                {
                    string xml = GeneratePropXml(propDrawables, ytdFiles, StripPrefix);
                    byte[] ymtData = CompileXmlToYmt(xml);
                    File.WriteAllBytes(Path.Combine(inputFolder, $"{packName}_props.ymt"), ymtData);
                }

                // Generate fxmanifest.lua
                GenerateFxManifest(inputFolder, packName, pedTarget, compDrawables.Count > 0, propDrawables.Count > 0);

                result.Success = true;
                result.OutputPath = inputFolder;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
            }

            return result;
        }

        private static string GenerateComponentXml(List<string> drawables, List<string> allTextures, Func<string, string> stripPrefix)
        {
            var sb = new StringBuilder();
            sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
            sb.AppendLine("<CPedComponentDictionary>");
            sb.AppendLine("  <components>");

            foreach (var ydd in drawables)
            {
                string cleanYdd = stripPrefix(ydd);
                sb.AppendLine("    <Item>");
                sb.AppendLine($"      <name>{cleanYdd}</name>");
                sb.AppendLine("      <drawables>");
                sb.AppendLine("        <Item>");
                sb.AppendLine($"          <name>{cleanYdd}</name>");
                sb.AppendLine($"          <drawable>{cleanYdd}</drawable>");
                sb.AppendLine("          <textures>");

                // Find matching textures. Typically a texture is named like jbib_diff_000_a_uni when the drawable is jbib_000_u
                // We just match the prefix part. e.g. jbib_diff_000
                string prefixMatch = cleanYdd;
                if (cleanYdd.Length > 5 && cleanYdd[4] == '_') 
                {
                    // e.g. jbib_000_u -> jbib_diff_000
                    string component = cleanYdd.Substring(0, 4); // jbib
                    string id = cleanYdd.Substring(5).Split('_')[0]; // 000
                    prefixMatch = $"{component}_diff_{id}";
                }

                var matchingTexs = allTextures.Where(t => stripPrefix(t).StartsWith(prefixMatch, StringComparison.OrdinalIgnoreCase)).ToList();
                
                // If no specific match, try finding anything that contains the ID or just add a default placeholder if none exists
                if (matchingTexs.Count == 0)
                {
                    sb.AppendLine("            <Item>");
                    sb.AppendLine($"              <name>{cleanYdd}</name>");
                    sb.AppendLine($"              <texId>{cleanYdd}</texId>");
                    sb.AppendLine("            </Item>");
                }
                else
                {
                    foreach (var tex in matchingTexs)
                    {
                        string cleanTex = stripPrefix(tex);
                        sb.AppendLine("            <Item>");
                        sb.AppendLine($"              <name>{cleanTex}</name>");
                        sb.AppendLine($"              <texId>{cleanTex}</texId>");
                        sb.AppendLine("            </Item>");
                    }
                }

                sb.AppendLine("          </textures>");
                sb.AppendLine("        </Item>");
                sb.AppendLine("      </drawables>");
                sb.AppendLine("    </Item>");
            }

            sb.AppendLine("  </components>");
            sb.AppendLine("</CPedComponentDictionary>");
            return sb.ToString();
        }

        private static string GeneratePropXml(List<string> drawables, List<string> allTextures, Func<string, string> stripPrefix)
        {
            var sb = new StringBuilder();
            sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
            sb.AppendLine("<CPedPropDictionary>");
            sb.AppendLine("  <props>");

            foreach (var ydd in drawables)
            {
                string cleanYdd = stripPrefix(ydd);
                sb.AppendLine("    <Item>");
                sb.AppendLine($"      <name>{cleanYdd}</name>");
                sb.AppendLine("      <drawables>");
                sb.AppendLine("        <Item>");
                sb.AppendLine($"          <name>{cleanYdd}</name>");
                sb.AppendLine($"          <drawable>{cleanYdd}</drawable>");
                sb.AppendLine("          <textures>");

                string prefixMatch = cleanYdd.Replace(".ydd", "").Replace("_u", "").Replace("_a", "");
                
                var matchingTexs = allTextures.Where(t => stripPrefix(t).StartsWith(prefixMatch, StringComparison.OrdinalIgnoreCase)).ToList();

                if (matchingTexs.Count == 0)
                {
                    sb.AppendLine("            <Item>");
                    sb.AppendLine($"              <name>{cleanYdd}</name>");
                    sb.AppendLine($"              <texId>{cleanYdd}</texId>");
                    sb.AppendLine("            </Item>");
                }
                else
                {
                    foreach (var tex in matchingTexs)
                    {
                        string cleanTex = stripPrefix(tex);
                        sb.AppendLine("            <Item>");
                        sb.AppendLine($"              <name>{cleanTex}</name>");
                        sb.AppendLine($"              <texId>{cleanTex}</texId>");
                        sb.AppendLine("            </Item>");
                    }
                }

                sb.AppendLine("          </textures>");
                sb.AppendLine("        </Item>");
                sb.AppendLine("      </drawables>");
                sb.AppendLine("    </Item>");
            }

            sb.AppendLine("  </props>");
            sb.AppendLine("</CPedPropDictionary>");
            return sb.ToString();
        }

        private static byte[] CompileXmlToYmt(string xml)
        {
            var doc = new XmlDocument();
            doc.LoadXml(xml);
            var meta = XmlMeta.GetMeta(doc);
            if (meta == null) throw new Exception("CodeWalker failed to parse generated YMT XML.");
            return ResourceBuilder.Build(meta, 2);
        }

        private static void GenerateFxManifest(string folder, string packName, string pedTarget, bool hasClothes, bool hasProps)
        {
            var sb = new StringBuilder();
            sb.AppendLine("fx_version 'cerulean'");
            sb.AppendLine("game 'gta5'");
            sb.AppendLine();
            sb.AppendLine($"description 'TGToolKit Generated Clothing Pack: {packName}'");
            sb.AppendLine();
            
            sb.AppendLine("files {");
            if (hasClothes) sb.AppendLine($"    '{packName}_clothes.ymt',");
            if (hasProps) sb.AppendLine($"    '{packName}_props.ymt',");
            sb.AppendLine("}");
            sb.AppendLine();
            sb.AppendLine("data_file 'PED_METADATA_FILE' 'stream/*'");
            
            if (hasClothes)
                sb.AppendLine($"data_file 'SHOP_PED_APPAREL_META_FILE' '{packName}_clothes.ymt'");
            
            if (hasProps)
                sb.AppendLine($"data_file 'SHOP_PED_APPAREL_META_FILE' '{packName}_props.ymt'");

            File.WriteAllText(Path.Combine(folder, "fxmanifest.lua"), sb.ToString());
        }
    }
}
