using System.Collections.Generic;

namespace ToolKitV.Models
{
    public class IntegrationRecipe
    {
        public string RecipeId           { get; set; } = string.Empty;   // e.g., "jpr-inventory_to_qbx_core"
        public string RequiredResource   { get; set; } = string.Empty;   // e.g., "jpr-inventory"
        public string TargetResource     { get; set; } = string.Empty;   // e.g., "qbx_core"
        public string Description        { get; set; } = string.Empty;

        public List<FilePatch> Patches { get; set; } = new List<FilePatch>();
    }

    public class FilePatch
    {
        public string TargetFilePath { get; set; } = string.Empty;   // e.g., "bridge/qb/shared/main.lua"
        public string SearchSnippet  { get; set; } = string.Empty;   // The exact code to find
        public string ReplaceWith    { get; set; } = string.Empty;   // The new code
        public bool   IsRegex        { get; set; }                   // If true, uses Regex matching
    }
}
