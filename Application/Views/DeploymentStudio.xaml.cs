using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using ToolKitV.Models;
using ToolKitV.Models.Providers;

namespace ToolKitV.Views
{
    public partial class DeploymentStudio : UserControl
    {
        private bool _isSftp = false;
        private int _currentStep = 1;

        public DeploymentStudio()
        {
            InitializeComponent();
            LogWriter.OnLog += LogWriter_OnLog;
            Unloaded += DeploymentStudio_Unloaded;
        }

        private void DeploymentStudio_Unloaded(object sender, RoutedEventArgs e)
        {
            LogWriter.OnLog -= LogWriter_OnLog;
        }

        private void LogWriter_OnLog(string message)
        {
            Dispatcher.Invoke(() =>
            {
                SystemLogBox.AppendText($"{DateTime.Now:HH:mm:ss} | {message}\n");
                SystemLogBox.ScrollToEnd();

                // Checkpoint logic based on log prints
                UpdateCheckpointStates(message);
            });
        }

        private void UpdateCheckpointStates(string msg)
        {
            if (msg.Contains("Initiating Phase 0: Temporal Failsafe"))
            {
                CheckIcon1.Text = "⏳";
                CheckIcon1.Foreground = System.Windows.Media.Brushes.Gold;
            }
            else if (msg.Contains("Pre-deployment snapshot secured"))
            {
                CheckIcon1.Text = "✓";
                CheckIcon1.Foreground = System.Windows.Media.Brushes.LimeGreen;
            }
            else if (msg.Contains("Running Static Code Diagnostics"))
            {
                CheckIcon2.Text = "⏳";
                CheckIcon2.Foreground = System.Windows.Media.Brushes.Gold;
            }
            else if (msg.Contains("Syntax check done"))
            {
                CheckIcon2.Text = "✓";
                CheckIcon2.Foreground = System.Windows.Media.Brushes.LimeGreen;
            }
            else if (msg.Contains("Running Manifest Auto-Fixer"))
            {
                CheckIcon3.Text = "⏳";
                CheckIcon3.Foreground = System.Windows.Media.Brushes.Gold;
            }
            else if (msg.Contains("Manifest fixes complete"))
            {
                CheckIcon3.Text = "✓";
                CheckIcon3.Foreground = System.Windows.Media.Brushes.LimeGreen;
            }
            else if (msg.Contains("Executing SQL Matrix"))
            {
                CheckIcon4.Text = "⏳";
                CheckIcon4.Foreground = System.Windows.Media.Brushes.Gold;
            }
            else if (msg.Contains("Database check complete"))
            {
                CheckIcon4.Text = "✓";
                CheckIcon4.Foreground = System.Windows.Media.Brushes.LimeGreen;
            }
            else if (msg.Contains("Harvesting items"))
            {
                CheckIcon5.Text = "⏳";
                CheckIcon5.Foreground = System.Windows.Media.Brushes.Gold;
            }
            else if (msg.Contains("Harvesting done") || msg.Contains("Successfully injected items") || msg.Contains("No new custom items found"))
            {
                CheckIcon5.Text = "✓";
                CheckIcon5.Foreground = System.Windows.Media.Brushes.LimeGreen;
            }
            else if (msg.Contains("Transpiling legacy code"))
            {
                CheckIcon6.Text = "⏳";
                CheckIcon6.Foreground = System.Windows.Media.Brushes.Gold;
            }
            else if (msg.Contains("Transpiled"))
            {
                CheckIcon6.Text = "✓";
                CheckIcon6.Foreground = System.Windows.Media.Brushes.LimeGreen;
            }
            else if (msg.Contains("Applying target integration bridge recipes"))
            {
                CheckIcon7.Text = "⏳";
                CheckIcon7.Foreground = System.Windows.Media.Brushes.Gold;
            }
            else if (msg.Contains("Integration recipes successfully applied"))
            {
                CheckIcon7.Text = "✓";
                CheckIcon7.Foreground = System.Windows.Media.Brushes.LimeGreen;
            }
            else if (msg.Contains("Syncing Master Locales"))
            {
                CheckIcon8.Text = "⏳";
                CheckIcon8.Foreground = System.Windows.Media.Brushes.Gold;
            }
            else if (msg.Contains("DEPLOYMENT COMPLETE"))
            {
                CheckIcon8.Text = "✓";
                CheckIcon8.Foreground = System.Windows.Media.Brushes.LimeGreen;
            }
        }

        private void ModeRadio_Changed(object sender, RoutedEventArgs e)
        {
            if (LocalPanel == null || SftpPanel == null) return;

            _isSftp = SftpModeRadio.IsChecked == true;
            LocalPanel.Visibility = _isSftp ? Visibility.Collapsed : Visibility.Visible;
            SftpPanel.Visibility = _isSftp ? Visibility.Visible : Visibility.Collapsed;
        }

        private void NextButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentStep == 1)
            {
                // Validate Step 1
                if (!_isSftp && string.IsNullOrWhiteSpace(LocalFolder.Path))
                {
                    MessageBox.Show("Please select a local server resources folder to continue.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                if (_isSftp && (string.IsNullOrWhiteSpace(SftpHost.TextValue) || string.IsNullOrWhiteSpace(SftpUsername.TextValue)))
                {
                    MessageBox.Show("Please fill out Host and Username SFTP credentials to continue.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                Step1Panel.Visibility = Visibility.Collapsed;
                Step2Panel.Visibility = Visibility.Visible;
                BackButton.Visibility = Visibility.Visible;
                NextButton.Visibility = Visibility.Collapsed;
                DeployButton.Visibility = Visibility.Visible;
                _currentStep = 2;
            }
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentStep == 2)
            {
                Step1Panel.Visibility = Visibility.Visible;
                Step2Panel.Visibility = Visibility.Collapsed;
                BackButton.Visibility = Visibility.Collapsed;
                NextButton.Visibility = Visibility.Visible;
                DeployButton.Visibility = Visibility.Collapsed;
                _currentStep = 1;
            }
        }

        private async void DeployButton_Click(object sender, RoutedEventArgs e)
        {
            // Collect configurations
            string workspacePath = _isSftp ? SftpRootPath.TextValue : LocalFolder.Path;

            if (string.IsNullOrWhiteSpace(workspacePath))
            {
                MessageBox.Show("Please specify a valid workspace path first.", "Zero-to-Hero", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var confirm = MessageBox.Show(
                "Are you sure you want to run the Zero-to-Hero Server deployment?\n\n" +
                "⚠️ This will compile, auto-fix, transpile, and bridge your codebase.\n" +
                "A Safety Temporal Failsafe backup (.zip) will be captured before any files are modified.",
                "TGToolKit Deployment",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirm != MessageBoxResult.Yes) return;

            // Lock UI controls
            BackButton.IsEnabled = false;
            DeployButton.IsEnabled = false;
            DefaultHeaderCard.Visibility = Visibility.Collapsed;
            ProgressHeaderCard.Visibility = Visibility.Visible;

            // Reset Checkpoints Visuals
            ResetCheckpoints();

            SystemLogBox.Text = string.Empty;

            IFileSystemProvider? fs = null;
            try
            {
                if (_isSftp)
                {
                    int sftpPort = int.TryParse(SftpPort.Value, out int portVal) ? portVal : 22;
                    fs = new SftpFileSystemProvider(SftpHost.TextValue, sftpPort, SftpUsername.TextValue, SftpPassword.Password);
                }
                else
                {
                    fs = new LocalFileSystemProvider();
                }

                var orchestrator = new SetupOrchestrator();
                var log = new LogWriter("=== Initializing Zero-To-Hero Orchestrator Pipeline ===");

                // Build deployment configuration
                var config = new DeploymentConfig
                {
                    MySqlConnectionString = MySqlConnectionStringInput.TextValue,
                    TargetInventory = GetInventoryName(),
                    MasterLocale = GetLocaleName(),
                    DiscordWebhook = MasterWebhookInput.TextValue,
                    GlobalRecipes = new System.Collections.Generic.List<IntegrationRecipe>(),
                    SpecificRecipes = new System.Collections.Generic.List<IntegrationRecipe>()
                };

                // Load integration recipes
                string recipeDir = AppPaths.RecipesFolder;
                if (Directory.Exists(recipeDir))
                {
                    var allRecipes = new RecipeManager();
                    await allRecipes.LoadAllRecipesAsync(log);

                    config.GlobalRecipes = allRecipes.ActiveRecipes.Where(r => r.RequiredResource == "*").ToList();
                    config.SpecificRecipes = allRecipes.ActiveRecipes.Where(r => r.RequiredResource != "*").ToList();
                }

                bool success = await Task.Run(() => orchestrator.RunZeroToHeroDeploymentAsync(fs, workspacePath, config, log));

                if (success)
                {
                    MessageBox.Show(
                        "Zero-To-Hero Deployment completed successfully!\n\n" +
                        "Your FiveM server ecosystem has been completely standardized and modernised.\n" +
                        "A detailed deployment_report.txt has been dropped onto your Desktop.",
                        "Deployment Complete",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show(
                        "Deployment encountered a critical failure. The temporal failsafe was automatically triggered to restore the pre-deployment state.\n" +
                        "Please check the log monitor for details.",
                        "Deployment Failed",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Deployment initialization error:\n{ex.Message}", "Deployment Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                fs?.Disconnect();
                BackButton.IsEnabled = true;
                DeployButton.IsEnabled = true;
                DefaultHeaderCard.Visibility = Visibility.Visible;
                ProgressHeaderCard.Visibility = Visibility.Collapsed;
            }
        }

        private void ResetCheckpoints()
        {
            TextBlock[] icons = { CheckIcon1, CheckIcon2, CheckIcon3, CheckIcon4, CheckIcon5, CheckIcon6, CheckIcon7, CheckIcon8 };
            foreach (var icon in icons)
            {
                icon.Text = "⚪";
                icon.Foreground = System.Windows.Media.Brushes.Gray;
            }
        }

        private string GetInventoryName()
        {
            if (InventoryCombo.SelectedItem is ComboBoxItem item)
            {
                string text = item.Content.ToString() ?? "";
                if (text.Contains("ox_inventory")) return "ox_inventory";
                if (text.Contains("jpr-inventory")) return "jpr-inventory";
                if (text.Contains("qs-inventory")) return "qs-inventory";
                if (text.Contains("qb-inventory")) return "qb-inventory";
            }
            return "ox_inventory";
        }

        private string GetLocaleName()
        {
            if (LocaleCombo.SelectedItem is ComboBoxItem item)
            {
                string text = item.Content.ToString() ?? "";
                return text.Split(' ')[0]; // Returns en, es, fr, etc.
            }
            return "en";
        }
    }
}
