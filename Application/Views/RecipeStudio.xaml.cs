using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using ToolKitV.Models;

namespace ToolKitV.Views
{
    public partial class RecipeStudio : UserControl
    {
        private string _testFilePath = string.Empty;

        public RecipeStudio()
        {
            InitializeComponent();
        }

        private void btnSelectTestFile_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog ofd = new OpenFileDialog
            {
                Filter = "Lua Files (*.lua)|*.lua|All Files (*.*)|*.*",
                Title = "Select a Lua file to test the patch"
            };

            if (ofd.ShowDialog() == true)
            {
                _testFilePath = ofd.FileName;
                txtTestFileName.Text = Path.GetFileName(_testFilePath);
                btnRunTest.IsEnabled = true;
            }
        }

        private void btnRunTest_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(txtSearch.Text))
            {
                txtValidationResult.Foreground = System.Windows.Media.Brushes.Red;
                txtValidationResult.Text = "[FAIL] Search snippet is empty.";
                return;
            }

            try
            {
                string fileContent = File.ReadAllText(_testFilePath);
                string originalContent = fileContent;

                // Use the exact same engine the Auto-Wirer uses
                string flexPattern = BridgeEngine.BuildBulletproofRegex(txtSearch.Text);
                
                int matchCount = Regex.Matches(fileContent, flexPattern).Count;

                if (matchCount == 0)
                {
                    txtValidationResult.Foreground = System.Windows.Media.Brushes.Orange;
                    txtValidationResult.Text = $"[NO MATCH]\nThe engine could not find the snippet in the selected file.\n\nGenerated Regex:\n{flexPattern}";
                }
                else
                {
                    // Simulate the patch
                    fileContent = Regex.Replace(fileContent, flexPattern, txtReplace.Text);
                    
                    txtValidationResult.Foreground = System.Windows.Media.Brushes.LimeGreen;
                    txtValidationResult.Text = $"[SUCCESS]\nFound {matchCount} match(es).\nPatch applied successfully in memory.\n\nReady for JSON Export.";
                }
            }
            catch (Exception ex)
            {
                txtValidationResult.Foreground = System.Windows.Media.Brushes.Red;
                txtValidationResult.Text = $"[ERROR]\n{ex.Message}";
            }
        }

        private void btnExport_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(txtRecipeId.Text) || string.IsNullOrWhiteSpace(txtSearch.Text))
            {
                MessageBox.Show("Recipe ID and Search Snippet are required.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 1. Build the Object cleanly
            var recipe = new IntegrationRecipe
            {
                RecipeId = txtRecipeId.Text.Trim(),
                RequiredResource = txtRequired.Text.Trim(),
                TargetResource = txtTarget.Text.Trim(),
                Description = txtDescription.Text.Trim(),
                Patches = new List<FilePatch>
                {
                    new FilePatch
                    {
                        TargetFilePath = txtFilePath.Text.Trim(),
                        // Notice we do NOT escape anything manually. We pass the raw text from the TextBox.
                        SearchSnippet = txtSearch.Text, 
                        ReplaceWith = txtReplace.Text,
                        IsRegex = false
                    }
                }
            };

            // 2. Let System.Text.Json handle 100% of the escaping and formatting
            var options = new JsonSerializerOptions 
            { 
                WriteIndented = true,
                // Ensures non-ASCII characters are not unicode-escaped unnecessarily 
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping 
            };
            
            string finalJson = JsonSerializer.Serialize(recipe, options);

            // Ensure Recipes directory exists
            string recipesDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Recipes");
            if (!Directory.Exists(recipesDir))
            {
                Directory.CreateDirectory(recipesDir);
            }

            // 3. Save it to disk
            SaveFileDialog sfd = new SaveFileDialog
            {
                Filter = "JSON Recipe (*.json)|*.json",
                FileName = $"{recipe.RecipeId}.json",
                InitialDirectory = recipesDir
            };

            if (sfd.ShowDialog() == true)
            {
                File.WriteAllText(sfd.FileName, finalJson);
                MessageBox.Show($"Recipe saved successfully to:\n{sfd.FileName}", "Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
    }
}
