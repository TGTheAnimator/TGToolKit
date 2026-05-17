using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace ToolKitV.Models
{
    public class HarvestedItem
    {
        public string SpawnCode { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public float Weight { get; set; }
        public string ImageFileName { get; set; } = string.Empty;
        public string LocalImagePath { get; set; } = string.Empty;
        
        // This holds the exact code block, but users can edit it to change metadata/stats
        public string RawLuaSnippet { get; set; } = string.Empty;
    }

    public static class ItemHarvester
    {
        public static List<HarvestedItem> HarvestFromDirectory(string scriptDirectory)
        {
            var items = new List<HarvestedItem>();
            
            // 1. Hunt for the items.lua or config.lua file
            string[] possibleFiles = Directory.GetFiles(scriptDirectory, "*.lua", SearchOption.AllDirectories);
            
            foreach (var file in possibleFiles)
            {
                string text = File.ReadAllText(file);

                // Matches standard QBCore/Ox item blocks: ['laptop'] = { label = 'Laptop', weight = 1000 ... }
                // Also handles multi-line declarations.
                var matches = Regex.Matches(text, @"\['(.*?)'\]\s*=\s*\{([^}]*)\}");

                foreach (Match match in matches)
                {
                    string code = match.Groups[1].Value;
                    string properties = match.Groups[2].Value;

                    // Extract properties
                    var labelMatch = Regex.Match(properties, @"label\s*=\s*['""](.*?)['""]");
                    var weightMatch = Regex.Match(properties, @"weight\s*=\s*([0-9.]+)");
                    var imageMatch = Regex.Match(properties, @"image\s*=\s*['""](.*?)['""]");

                    string imageName = imageMatch.Success ? imageMatch.Groups[1].Value : $"{code}.png";

                    // 2. Hunt for the actual .png file in this script's folder
                    string imagePath = FindImageFile(scriptDirectory, imageName);

                    items.Add(new HarvestedItem
                    {
                        SpawnCode = code,
                        Label = labelMatch.Success ? labelMatch.Groups[1].Value : code,
                        Weight = weightMatch.Success ? float.Parse(weightMatch.Groups[1].Value) : 0,
                        ImageFileName = imageName,
                        LocalImagePath = imagePath,
                        RawLuaSnippet = match.Value // The full block including metadata
                    });
                }
            }

            return items;
        }

        private static string FindImageFile(string rootDir, string imageName)
        {
            string[] images = Directory.GetFiles(rootDir, imageName, SearchOption.AllDirectories);
            return images.Length > 0 ? images[0] : string.Empty;
        }
    }
}
