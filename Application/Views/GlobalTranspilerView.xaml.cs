using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using ToolKitV.Models;
using ToolKitV.Models.Providers;

namespace ToolKitV.Views
{
    public partial class GlobalTranspilerView : UserControl
    {
        public class RecipeViewModel
        {
            public IntegrationRecipe Recipe { get; set; } = null!;
            public bool IsActive { get; set; } = true;
        }

        private ServerLinter _linterInstance;

        public GlobalTranspilerView(ServerLinter linterInstance)
        {
            InitializeComponent();
            _linterInstance = linterInstance;
            LoadRecipes();
        }

        private void LoadRecipes()
        {
            var recipes = GlobalTranspiler.LoadGlobalRecipes();
            var viewModels = recipes.Select(r => new RecipeViewModel { Recipe = r, IsActive = true }).ToList();
            RecipesList.ItemsSource = viewModels;
        }

        private async void RunGlobalButton_Click(object sender, RoutedEventArgs e)
        {
            // Gather SFTP credentials from the Linter instance
            var provider = _linterInstance.GetConfiguredProvider();
            string rootPath = _linterInstance.GetRootPath();

            if (provider == null || string.IsNullOrWhiteSpace(rootPath))
            {
                MessageBox.Show("Please connect to a workspace via the Server Linter tab first.", "Workspace Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Gather active recipes
            var vms = RecipesList.ItemsSource as List<RecipeViewModel>;
            var activeRecipes = vms?.Where(vm => vm.IsActive).Select(vm => vm.Recipe).ToList() ?? new List<IntegrationRecipe>();

            // Gather Overrides
            string webhook = WebhookInput.Text;
            string locale = LocaleInput.Text;
            string currency = CurrencyInput.Text;
            string icon = CurrencyIconInput.Text;

            if (activeRecipes.Count == 0 && string.IsNullOrWhiteSpace(webhook) && string.IsNullOrWhiteSpace(locale) && string.IsNullOrWhiteSpace(currency) && string.IsNullOrWhiteSpace(icon))
            {
                MessageBox.Show("Please select at least one recipe or input an override value to transpile.", "Nothing to do", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var confirm = MessageBox.Show($"You are about to transpile the entire workspace at:\n{rootPath}\n\nThis will permanently modify files and cannot be undone (though .tg_backup files will be created).\n\nAre you sure you want to proceed?", "Confirm Mass Transpilation", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            
            if (confirm != MessageBoxResult.Yes) return;

            RunGlobalButton.IsEnabled = false;
            LoadingOverlay.Visibility = Visibility.Visible;
            ScanProgress.Value = 0;

            var progress = new Progress<int>(p => 
            {
                ScanProgress.Value = p;
                StatusText.Text = $"Transpiling... {p}%";
            });

            try
            {
                var log = new LogWriter("=== Global Transpiler Pass ===");
                int modifiedCount = await GlobalTranspiler.RunGlobalPassAsync(
                    provider, 
                    rootPath, 
                    activeRecipes, 
                    webhook, 
                    locale, 
                    currency, 
                    icon, 
                    log, 
                    progress);

                string auditPath = AppPaths.AuditLogFilePath;
                MessageBox.Show($"Mass Transpilation Complete!\n\nModified {modifiedCount} file(s) across the server.\n\nAudit log saved to:\n{auditPath}", "Transpiler Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Fatal error during mass transpilation:\n{ex.Message}", "Transpiler Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                RunGlobalButton.IsEnabled = true;
                LoadingOverlay.Visibility = Visibility.Collapsed;
            }
        }
    }
}
