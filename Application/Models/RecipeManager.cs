using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace ToolKitV.Models
{
    public class RecipeManager
    {
        private readonly string _recipeDirectory;
        public List<IntegrationRecipe> ActiveRecipes { get; private set; } = new List<IntegrationRecipe>();

        public RecipeManager()
        {
            // Assuming a 'Recipes' folder next to the TGToolKit executable
            _recipeDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Recipes");
            if (!Directory.Exists(_recipeDirectory))
            {
                Directory.CreateDirectory(_recipeDirectory);
            }
        }

        public async Task LoadAllRecipesAsync(LogWriter log)
        {
            ActiveRecipes.Clear();
            string[] recipeFiles = Directory.GetFiles(_recipeDirectory, "*.json");

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

            foreach (var file in recipeFiles)
            {
                try
                {
                    string json = await File.ReadAllTextAsync(file);
                    var recipe = JsonSerializer.Deserialize<IntegrationRecipe>(json, options);
                    
                    if (recipe != null && !string.IsNullOrEmpty(recipe.TargetResource))
                    {
                        ActiveRecipes.Add(recipe);
                    }
                }
                catch (Exception ex)
                {
                    log.LogWrite($"[ERROR] Failed to parse recipe file {Path.GetFileName(file)}: {ex.Message}");
                }
            }

            log.LogWrite($"[SYSTEM] Loaded {ActiveRecipes.Count} Integration Recipes.");
        }
        public static List<IntegrationRecipe> GetApplicableRecipes(string recipeDir, List<string> availableResources)
        {
            var applicable = new List<IntegrationRecipe>();
            if (!Directory.Exists(recipeDir)) return applicable;

            string[] recipeFiles = Directory.GetFiles(recipeDir, "*.json");
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

            foreach (var file in recipeFiles)
            {
                try
                {
                    string json = File.ReadAllText(file);
                    var recipe = JsonSerializer.Deserialize<IntegrationRecipe>(json, options);
                    
                    if (recipe != null && !string.IsNullOrEmpty(recipe.TargetResource) && !string.IsNullOrEmpty(recipe.RequiredResource))
                    {
                        // Check if both the target resource to patch AND the required ecosystem resource exist in the workspace
                        if (availableResources.Contains(recipe.TargetResource, StringComparer.OrdinalIgnoreCase) && 
                            availableResources.Contains(recipe.RequiredResource, StringComparer.OrdinalIgnoreCase))
                        {
                            applicable.Add(recipe);
                        }
                    }
                }
                catch
                {
                    // Ignore parse failures for applicability scans
                }
            }

            return applicable;
        }
    }
}
