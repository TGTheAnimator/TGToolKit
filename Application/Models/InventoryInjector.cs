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
            // 1. Determine paths based on the target inventory
            string inventoryDataPath = "";
            string inventoryImagePath = "";

            if (targetInventory == "ox_inventory")
            {
                inventoryDataPath = $"{serverRootPath}/{targetInventory}/data/items.lua";
                inventoryImagePath = $"{serverRootPath}/{targetInventory}/web/images";
            }
            else if (targetInventory == "qb-core / qb-inventory")
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
                    
                    // Works perfectly for both SftpFileSystemProvider (WinSCP) and LocalFileSystemProvider
                    await fs.UploadFileAsync(item.LocalImagePath, remoteImagePath);
                    
                    auditLog.LogChange(remoteImagePath, "Image Uploaded", $"Injected image for {item.SpawnCode}");
                }
            }

            // 3. Append the items to the Lua file
            string currentItemsFile = await fs.ReadAllTextAsync(inventoryDataPath);
            
            // Back up the file just in case
            await fs.CreateBackupAsync(inventoryDataPath);

            // Strip the final closing bracket so we can insert items inside the table
            int lastBracketIndex = currentItemsFile.LastIndexOf('}');
            if (lastBracketIndex > -1)
            {
                string updatedFile = currentItemsFile.Substring(0, lastBracketIndex);
                
                updatedFile += "\n\t-- [ TGToolKit Auto-Injected Items ] --\n";
                foreach (var item in items)
                {
                    // Add the raw snippet, ensuring a trailing comma
                    // The RawLuaSnippet holds whatever they edited (stats, metadata, etc)
                    updatedFile += $"\t{item.RawLuaSnippet},\n";
                }
                
                updatedFile += "\n}\n"; // Close the table back up

                await fs.WriteAllTextAsync(inventoryDataPath, updatedFile);
                auditLog.LogChange(inventoryDataPath, "Items Appended", $"Added {items.Count} items to the master inventory.");
            }
        }
    }
}
