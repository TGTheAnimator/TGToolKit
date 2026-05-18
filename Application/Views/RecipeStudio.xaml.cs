using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
        
        // This collection holds all the file edits for the current recipe
        public ObservableCollection<FilePatch> CurrentPatches { get; set; }
        private bool _isUpdatingUI = false;

        public RecipeStudio()
        {
            InitializeComponent();
            CurrentPatches = new ObservableCollection<FilePatch>();
            lstPatches.ItemsSource = CurrentPatches;
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

                // Use the exact same engine the Auto-Wirer uses
                string flexPattern = chkIsRegex.IsChecked == true 
                    ? txtSearch.Text 
                    : BridgeEngine.BuildBulletproofRegex(txtSearch.Text);
                
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

        private void btnAddPatch_Click(object sender, RoutedEventArgs e)
        {
            var newPatch = new FilePatch { TargetFilePath = "new_file.lua" };
            CurrentPatches.Add(newPatch);
            lstPatches.SelectedItem = newPatch;
        }

        private void btnRemovePatch_Click(object sender, RoutedEventArgs e)
        {
            if (lstPatches.SelectedItem is FilePatch selected)
            {
                CurrentPatches.Remove(selected);
            }
        }

        private void lstPatches_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (lstPatches.SelectedItem is FilePatch selected)
            {
                // Temporarily disable the TextChanged event so we don't overwrite data while loading
                _isUpdatingUI = true; 
                
                txtFilePath.Text = selected.TargetFilePath;
                txtSearch.Text = selected.SearchSnippet;
                txtReplace.Text = selected.ReplaceWith;
                chkIsRegex.IsChecked = selected.IsRegex;
                
                PatchEditorGrid.IsEnabled = true;
                _isUpdatingUI = false;
            }
            else
            {
                // Lock the editor if nothing is selected
                PatchEditorGrid.IsEnabled = false;
                txtFilePath.Clear();
                txtSearch.Clear();
                txtReplace.Clear();
                chkIsRegex.IsChecked = false;
            }
        }

        private void Editor_TextChanged(object sender, RoutedEventArgs e)
        {
            // Save the textbox data to the currently selected patch object in memory
            if (_isUpdatingUI || lstPatches.SelectedItem is not FilePatch selected) return;

            selected.TargetFilePath = txtFilePath.Text;
            selected.SearchSnippet = txtSearch.Text;
            selected.ReplaceWith = txtReplace.Text;
            selected.IsRegex = chkIsRegex.IsChecked ?? false;

            // Force the ListBox to refresh the file name display
            lstPatches.Items.Refresh(); 
        }

        private void btnExport_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(txtRecipeId.Text))
            {
                MessageBox.Show("Recipe ID is required.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (CurrentPatches.Count == 0)
            {
                MessageBox.Show("Add at least one patch before exporting.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Assemble the final master recipe
            var recipe = new IntegrationRecipe
            {
                RecipeId = txtRecipeId.Text.Trim(),
                RequiredResource = txtRequired.Text.Trim(),
                TargetResource = txtTarget.Text.Trim(),
                Description = txtDescription.Text.Trim(),
                Patches = new List<FilePatch>(CurrentPatches)
            };

            // Convert to pretty JSON
            var options = new JsonSerializerOptions 
            { 
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping 
            };
            string finalJson = JsonSerializer.Serialize(recipe, options);

            // Ensure Recipes directory exists
            string recipesDir = AppPaths.RecipesFolder;
            if (!Directory.Exists(recipesDir))
            {
                Directory.CreateDirectory(recipesDir);
            }

            // Save it to disk
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
