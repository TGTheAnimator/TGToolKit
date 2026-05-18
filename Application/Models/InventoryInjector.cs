using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using ToolKitV.Models.Providers;

namespace ToolKitV.Models
{
    public static class InventoryInjector
    {
        public static async Task InjectItemsAsync(
            IFileSystemProvider fs, 
            string serverRootPath, 
            string targetInventory, 
            List<HarvestedItem> items, 
            AuditLogger auditLog)
        {
            string inventoryDataPath = "";
            string inventoryImagePath = "";

            // 1. Path Resolution
            if (targetInventory == "ox_inventory")
            {
                inventoryDataPath = $"{serverRootPath}/{targetInventory}/data/items.lua";
                inventoryImagePath = $"{serverRootPath}/{targetInventory}/web/images";
            }
            else if (targetInventory.Contains("qb-inventory") || targetInventory.Contains("qb-core"))
            {
                inventoryDataPath = $"{serverRootPath}/qb-core/shared/items.lua";
                inventoryImagePath = $"{serverRootPath}/qb-inventory/html/images"; 
            }
            else if (targetInventory == "jpr-inventory")
            {
                inventoryDataPath = $"{serverRootPath}/jpr-inventory/shared/items.lua"; 
                inventoryImagePath = $"{serverRootPath}/jpr-inventory/html/images";
            }
            else if (targetInventory == "qs-inventory")
            {
                inventoryDataPath = $"{serverRootPath}/qs-inventory/shared/items.lua"; 
                inventoryImagePath = $"{serverRootPath}/qs-inventory/html/images";
            }

            // 2. Upload the images via the active provider (SFTP/Local)
            foreach (var item in items)
            {
                if (!string.IsNullOrEmpty(item.LocalImagePath) && File.Exists(item.LocalImagePath))
                {
                    string remoteImagePath = $"{inventoryImagePath}/{item.ImageFileName}";
                    await fs.UploadFileAsync(item.LocalImagePath, remoteImagePath);
                    auditLog.LogChange(remoteImagePath, "Image Uploaded", $"Injected image for {item.SpawnCode}");
                }
            }

            // 3. Extract, Transpile, and Inject into the Lua file
            string currentItemsFile = await fs.ReadAllTextAsync(inventoryDataPath);
            await fs.CreateBackupAsync(inventoryDataPath);

            int lastBracketIndex = currentItemsFile.LastIndexOf('}');
            if (lastBracketIndex > -1)
            {
                string updatedFile = currentItemsFile.Substring(0, lastBracketIndex);
                updatedFile += "\n\t-- [ TGToolKit Auto-Transpiled Items ] --\n";
                
                foreach (var item in items)
                {
                    // Transpile the item into the exact framework requirement
                    string transpiledLua = TranspileToTargetFormat(item, targetInventory);
                    updatedFile += $"{transpiledLua}\n";
                }
                
                updatedFile += "\n}\n"; 

                await fs.WriteAllTextAsync(inventoryDataPath, updatedFile);
                auditLog.LogChange(inventoryDataPath, "Items Appended", $"Transpiled and injected {items.Count} items into {targetInventory}.");
            }
        }

        // --- THE FORMAT GENERATOR ---
        private static string TranspileToTargetFormat(HarvestedItem item, string targetInventory)
        {
            // Fallbacks for missing metadata
            float weight = item.Weight > 0 ? item.Weight : 100; // Default 100g if missing
            string label = string.IsNullOrEmpty(item.Label) ? item.SpawnCode : item.Label;

            if (targetInventory == "ox_inventory")
            {
                // Ox is minimal: ['code'] = { label = 'Name', weight = 100 }
                return $"\t['{item.SpawnCode}'] = {{\n\t\tlabel = '{label}',\n\t\tweight = {weight},\n\t\tdescription = 'Imported by TGToolKit'\n\t}},";
            }
            else if (targetInventory == "jpr-inventory" || targetInventory.Contains("qb-inventory"))
            {
                // JPR & QB require heavy one-line metadata injection
                return $"\t['{item.SpawnCode}'] = {{ name = '{item.SpawnCode}', label = '{label}', weight = {weight}, type = 'item', image = '{item.ImageFileName}', unique = false, useable = true, shouldClose = true, combinable = nil, description = 'Imported by TGToolKit' }},";
            }
            else if (targetInventory == "qs-inventory")
            {
                // QS uses strict bracket notation for inner properties
                return $"\t['{item.SpawnCode}'] = {{\n\t\t['name'] = '{item.SpawnCode}',\n\t\t['label'] = '{label}',\n\t\t['weight'] = {weight},\n\t\t['type'] = 'item',\n\t\t['image'] = '{item.ImageFileName}',\n\t\t['unique'] = false,\n\t\t['useable'] = true,\n\t\t['shouldClose'] = true,\n\t\t['combinable'] = nil,\n\t\t['description'] = 'Imported by TGToolKit'\n\t}},";
            }

            // Default fail-safe
            return $"\t['{item.SpawnCode}'] = {{ label = '{label}' }},";
        }
    }
}
