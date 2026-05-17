using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ToolKitV.Models.Providers;

namespace ToolKitV.Models
{
    public static class EconomyEngine
    {
        public static async Task<List<EconomyItem>> BuildEconomyMatrixAsync(
            IFileSystemProvider fs, string serverRootPath, IProgress<string> progress)
        {
            var items = new Dictionary<string, EconomyItem>();

            progress?.Report("Searching for master items.lua files...");
            var inventoryFiles = await fs.DiscoverFilesAsync(serverRootPath, "items.lua");

            int scanned = 0;
            foreach (var file in inventoryFiles)
            {
                scanned++;
                progress?.Report($"Parsing items.lua ({scanned}/{inventoryFiles.Count})...");

                string text = await fs.ReadAllTextAsync(file);

                // Matches standard item tables, like: ['water'] = {, ["water"] = {, water = {
                var matches = Regex.Matches(text, @"(?:\['|[""']?)([a-zA-Z0-9_\-]+)(?:'\]|[""']?)\s*=\s*\{");

                foreach (Match match in matches)
                {
                    string code = match.Groups[1].Value;
                    int startIdx = match.Index; // Start of entry
                    int braceStartIdx = match.Index + match.Length; // Start of contents after opening {

                    // Brace matching algorithm to get exact outer table scope
                    int braceCount = 1;
                    int endIdx = -1;
                    for (int i = braceStartIdx; i < text.Length; i++)
                    {
                        if (text[i] == '{') braceCount++;
                        else if (text[i] == '}')
                        {
                            braceCount--;
                            if (braceCount == 0)
                            {
                                endIdx = i + 1; // Include the closing }
                                break;
                            }
                        }
                    }

                    if (endIdx != -1)
                    {
                        string entryBlock = text.Substring(startIdx, endIdx - startIdx);

                        // Extract Label
                        var labelMatch = Regex.Match(entryBlock, @"label\s*=\s*['""](.*?)['""]");
                        // Extract Weight
                        var weightMatch = Regex.Match(entryBlock, @"weight\s*=\s*([0-9.]+)");

                        string label = labelMatch.Success ? labelMatch.Groups[1].Value : code;
                        float weight = 0f;
                        if (weightMatch.Success)
                        {
                            float.TryParse(weightMatch.Groups[1].Value, out weight);
                        }

                        // Ignore framework default setups if already loaded
                        if (!items.ContainsKey(code))
                        {
                            items[code] = new EconomyItem
                            {
                                SpawnCode = code,
                                Label = label,
                                Weight = weight,
                                DefinitionFilePath = file
                            };
                        }
                    }
                }
            }

            // 2. Scan shop scripts for buy prices
            progress?.Report("Searching for shop configuration files...");
            var configFiles = await fs.DiscoverFilesAsync(serverRootPath, "*config*.lua");
            var shopFiles   = await fs.DiscoverFilesAsync(serverRootPath, "*shop*.lua");
            var allShopFiles = configFiles.Union(shopFiles).Distinct().ToList();

            scanned = 0;
            foreach (var file in allShopFiles)
            {
                scanned++;
                if (scanned % 5 == 0)
                {
                    progress?.Report($"Scanning shops ({scanned}/{allShopFiles.Count})...");
                }

                try
                {
                    string shopText = await fs.ReadAllTextAsync(file);

                    // Pattern 1: { name = 'water', price = 15 }
                    var matches1 = Regex.Matches(shopText, @"\{\s*(?:name|item)\s*=\s*['""]([a-zA-Z0-9_\-]+)['""]\s*,\s*(?:price|buyPrice|cost)\s*=\s*([0-9]+)");
                    foreach (Match m in matches1)
                    {
                        string code = m.Groups[1].Value;
                        if (int.TryParse(m.Groups[2].Value, out int price))
                        {
                            if (items.TryGetValue(code, out var item))
                            {
                                item.BuyPrice = price;
                                item.ShopFilePath = file;
                            }
                        }
                    }

                    // Pattern 2: ['water'] = { price = 10 }
                    var matches2 = Regex.Matches(shopText, @"\['([a-zA-Z0-9_\-]+)'\]\s*=\s*\{\s*(?:price|buyPrice|cost)\s*=\s*([0-9]+)");
                    foreach (Match m in matches2)
                    {
                        string code = m.Groups[1].Value;
                        if (int.TryParse(m.Groups[2].Value, out int price))
                        {
                            if (items.TryGetValue(code, out var item))
                            {
                                item.BuyPrice = price;
                                item.ShopFilePath = file;
                            }
                        }
                    }
                }
                catch { /* Skip permission or read issues */ }
            }

            // 3. Scan pawnshops/recylers for sell prices
            progress?.Report("Searching for recyclers and sell points...");
            var pawnFiles    = await fs.DiscoverFilesAsync(serverRootPath, "*pawn*.lua");
            var recycleFiles = await fs.DiscoverFilesAsync(serverRootPath, "*recycle*.lua");
            var dealerFiles  = await fs.DiscoverFilesAsync(serverRootPath, "*dealer*.lua");
            var allSellFiles = pawnFiles.Union(recycleFiles).Union(dealerFiles).Distinct().ToList();

            foreach (var file in allSellFiles)
            {
                try
                {
                    string sellText = await fs.ReadAllTextAsync(file);

                    // Matches sell arrays: { name = 'water', price = 10 }
                    var matches1 = Regex.Matches(sellText, @"\{\s*(?:name|item)\s*=\s*['""]([a-zA-Z0-9_\-]+)['""]\s*,\s*(?:price|sellPrice|sell|value)\s*=\s*([0-9]+)");
                    foreach (Match m in matches1)
                    {
                        string code = m.Groups[1].Value;
                        if (int.TryParse(m.Groups[2].Value, out int sellPrice))
                        {
                            if (items.TryGetValue(code, out var item))
                            {
                                item.SellPrice = sellPrice;
                            }
                        }
                    }

                    // Matches flat mappings: ['water'] = 10
                    var matches2 = Regex.Matches(sellText, @"\['([a-zA-Z0-9_\-]+)'\]\s*=\s*([0-9]+)");
                    foreach (Match m in matches2)
                    {
                        string code = m.Groups[1].Value;
                        if (int.TryParse(m.Groups[2].Value, out int sellPrice))
                        {
                            if (items.TryGetValue(code, out var item))
                            {
                                item.SellPrice = sellPrice;
                            }
                        }
                    }
                }
                catch { /* Skip permission errors */ }
            }

            progress?.Report("Economy matrix assembly complete.");
            return new List<EconomyItem>(items.Values);
        }

        public static async Task<int> SyncEconomyDataAsync(
            IFileSystemProvider fs,
            List<EconomyItem> modifiedItems,
            IProgress<string> progress)
        {
            int filesUpdated = 0;
            progress?.Report("Grouping modifications by target file...");

            // 1. Sync Definition Files (Weights)
            var defGroups = modifiedItems.Where(i => !string.IsNullOrEmpty(i.DefinitionFilePath))
                                         .GroupBy(i => i.DefinitionFilePath);

            foreach (var group in defGroups)
            {
                string filePath = group.Key;
                progress?.Report($"Syncing weights to definitions: {Path.GetFileName(filePath)}...");

                try
                {
                    string fileText = await fs.ReadAllTextAsync(filePath);
                    bool fileModified = false;

                    foreach (var item in group)
                    {
                        // Match block start securely
                        string startPattern = $@"(?:\['|[""']?){Regex.Escape(item.SpawnCode)}(?:'\]|[""']?)\s*=\s*\{{";
                        var startMatch = Regex.Match(fileText, startPattern);

                        if (startMatch.Success)
                        {
                            int startIdx = startMatch.Index;
                            int braceStartIdx = startMatch.Index + startMatch.Length;

                            // Brace match to isolate item block
                            int braceCount = 1;
                            int endIdx = -1;
                            for (int i = braceStartIdx; i < fileText.Length; i++)
                            {
                                if (fileText[i] == '{') braceCount++;
                                else if (fileText[i] == '}')
                                {
                                    braceCount--;
                                    if (braceCount == 0)
                                    {
                                        endIdx = i + 1;
                                        break;
                                    }
                                }
                            }

                            if (endIdx != -1)
                            {
                                string prefix = fileText.Substring(0, startIdx);
                                string block  = fileText.Substring(startIdx, endIdx - startIdx);
                                string suffix = fileText.Substring(endIdx);

                                string weightPattern = @"(weight\s*=\s*)([0-9.]+)";
                                if (Regex.IsMatch(block, weightPattern))
                                {
                                    string newBlock = Regex.Replace(block, weightPattern, $"${{1}}{item.Weight}");
                                    if (newBlock != block)
                                    {
                                        block = newBlock;
                                        fileText = prefix + block + suffix;
                                        fileModified = true;
                                    }
                                }
                            }
                        }
                    }

                    if (fileModified)
                    {
                        await fs.CreateBackupAsync(filePath);
                        await fs.WriteAllTextAsync(filePath, fileText);
                        filesUpdated++;
                        progress?.Report($"[SUCCESS] Saved item definitions to {Path.GetFileName(filePath)}");
                    }
                }
                catch (Exception ex)
                {
                    progress?.Report($"[ERROR] Failed to save definitions to {Path.GetFileName(filePath)}: {ex.Message}");
                    throw;
                }
            }

            // 2. Sync Shop Price Configurations (Buy Prices)
            var shopGroups = modifiedItems.Where(i => !string.IsNullOrEmpty(i.ShopFilePath) && i.BuyPrice > 0)
                                          .GroupBy(i => i.ShopFilePath);

            foreach (var group in shopGroups)
            {
                string filePath = group.Key;
                progress?.Report($"Syncing prices to shops: {Path.GetFileName(filePath)}...");

                try
                {
                    string shopText = await fs.ReadAllTextAsync(filePath);
                    bool shopModified = false;

                    foreach (var item in group)
                    {
                        // Pattern 1: { name = 'water', price = 10 }
                        string pattern1 = $@"({{(?:\s*(?:name|item)\s*=\s*['""]{Regex.Escape(item.SpawnCode)}['""]\s*,\s*(?:price|buyPrice|cost)\s*=\s*))([0-9.]+)";
                        if (Regex.IsMatch(shopText, pattern1))
                        {
                            shopText = Regex.Replace(shopText, pattern1, $"${{1}}{item.BuyPrice}");
                            shopModified = true;
                        }

                        // Pattern 2: ['water'] = { price = 10 }
                        string pattern2 = $@"(\['{Regex.Escape(item.SpawnCode)}'\]\s*=\s*\{{\s*(?:price|buyPrice|cost)\s*=\s*)([0-9.]+)";
                        if (Regex.IsMatch(shopText, pattern2))
                        {
                            shopText = Regex.Replace(shopText, pattern2, $"${{1}}{item.BuyPrice}");
                            shopModified = true;
                        }
                    }

                    if (shopModified)
                    {
                        await fs.CreateBackupAsync(filePath);
                        await fs.WriteAllTextAsync(filePath, shopText);
                        filesUpdated++;
                        progress?.Report($"[SUCCESS] Saved shop configuration in {Path.GetFileName(filePath)}");
                    }
                }
                catch (Exception ex)
                {
                    progress?.Report($"[ERROR] Failed to save shops in {Path.GetFileName(filePath)}: {ex.Message}");
                    throw;
                }
            }

            progress?.Report("Economy synchronization complete.");
            return filesUpdated;
        }
    }
}
