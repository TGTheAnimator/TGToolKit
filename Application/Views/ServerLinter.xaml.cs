using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ToolKitV.Models;
using ToolKitV.Models.Providers;

namespace ToolKitV.Views
{
    public partial class ServerLinter : UserControl
    {
        public class IssueViewModel
        {
            public string ResourceName { get; set; } = string.Empty;
            public string Message      { get; set; } = string.Empty;
            public Brush  SeverityColor { get; set; } = Brushes.White;
        }

        private bool _isSftp = false;
        private Models.ServerLinter.LinterResult? _lastResult;
        private string _lastScannedDirectory = string.Empty;

        public ServerLinter()
        {
            InitializeComponent();
            LoadConfig();
        }

        private void LoadConfig()
        {
            var cfg = LinterConfig.Load();
            SftpHost.TextValue     = cfg.Host;
            SftpPort.Value         = cfg.Port.ToString();
            SftpUsername.TextValue = cfg.Username;
            SftpRootPath.TextValue = cfg.RootPath;
        }

        private void SaveConfig()
        {
            if (!_isSftp) return;

            new LinterConfig
            {
                Host     = SftpHost.TextValue,
                Port     = int.TryParse(SftpPort.Value, out int p2) ? p2 : 22,
                Username = SftpUsername.TextValue,
                RootPath = SftpRootPath.TextValue
            }.Save();
        }

        private void ModeRadio_Changed(object sender, RoutedEventArgs e)
        {
            _isSftp = SftpModeRadio?.IsChecked == true;
            if (LocalPanel != null)  LocalPanel.Visibility  = _isSftp ? Visibility.Collapsed : Visibility.Visible;
            if (SftpPanel != null)   SftpPanel.Visibility   = _isSftp ? Visibility.Visible   : Visibility.Collapsed;
            UpdateRunButton();
        }

        private void OnInputChanged(object? sender, EventArgs e)
            => UpdateRunButton();

        private void OnInputChanged(object? sender, PropertyChangedEventArgs e)
            => UpdateRunButton();

        private void UpdateRunButton()
        {
            if (RunLintButton == null) return;

            if (!_isSftp)
            {
                RunLintButton.IsButtonEnabledValue = !string.IsNullOrWhiteSpace(LocalFolder?.Path);
            }
            else
            {
                RunLintButton.IsButtonEnabledValue =
                    !string.IsNullOrWhiteSpace(SftpHost?.TextValue) &&
                    !string.IsNullOrWhiteSpace(SftpUsername?.TextValue) &&
                    !string.IsNullOrWhiteSpace(SftpRootPath?.TextValue) &&
                    (SftpPassword?.Password.Length > 0);
            }
        }

        private async void RunLintButton_Click(object sender, RoutedEventArgs e)
        {
            RunLintButton.IsButtonEnabledValue = false;
            CleanCard.Visibility               = Visibility.Collapsed;
            IssuesList.ItemsSource             = null;
            ResultScanned.Text                 = "…";
            ResultIssues.Text                  = "…";
            ResultWarnings.Text                = "…";

            try
            {
                SaveConfig();

                IManifestProvider provider;
                string rootPath;

                if (!_isSftp)
                {
                    provider = new LocalManifestProvider();
                    rootPath = LocalFolder.Path;
                    _lastScannedDirectory = rootPath; // Store for Auto-Wirer
                }
                else
                {
                    if (!int.TryParse(SftpPort.Value, out int port)) port = 22;

                    provider = new SftpManifestProvider(
                        SftpHost.TextValue,
                        port,
                        SftpUsername.TextValue,
                        SftpPassword.Password);

                    rootPath = SftpRootPath.TextValue;
                }

                var progress = new Progress<int>();
                var log      = new LogWriter("=== Server Linter started ===");

                Models.ServerLinter.LinterResult result =
                    await Models.ServerLinter.RunLinterAsync(provider, rootPath, progress, log);

                ResultScanned.Text  = result.ResourcesScanned.ToString();
                ResultIssues.Text   = result.ResourcesWithIssues.ToString();
                ResultWarnings.Text = result.Warnings.Count.ToString();

                if (result.Warnings.Count == 0)
                {
                    CleanCard.Visibility = Visibility.Visible;
                }
                else
                {
                    var vms = new List<IssueViewModel>();
                    foreach (var w in result.Warnings)
                    {
                        var color = w.Severity switch
                        {
                            Models.ServerLinter.Severity.Critical => new SolidColorBrush(Color.FromRgb(0xFF, 0x55, 0x55)),
                            Models.ServerLinter.Severity.Warning  => new SolidColorBrush(Color.FromRgb(0xFF, 0xB3, 0x47)),
                            _                                      => new SolidColorBrush(Color.FromRgb(0x55, 0xAA, 0xFF))
                        };

                        vms.Add(new IssueViewModel
                        {
                            ResourceName  = w.ResourceName,
                            Message       = w.Message,
                            SeverityColor = color
                        });
                    }

                    IssuesList.ItemsSource = vms;
                }

                // Store result so FixAll can act on it
                _lastResult = result;

                // ── Conflict detection ───────────────────────────────────────────────
                var detected = new Dictionary<ConflictCategory, List<string>>();
                foreach (var cat in ConflictDefinitions.Categories)
                {
                    var found = new List<string>();
                    foreach (var script in cat.MutuallyExclusiveScripts)
                        if (result.InstalledResources.Contains(script))
                            found.Add(script);

                    if (found.Count >= 2)
                        detected[cat] = found;
                }

                if (detected.Count > 0)
                {
                    var modal = new ConflictResolverWindow(detected, result.InstalledResources)
                    {
                        Owner = Window.GetWindow(this)
                    };

                    if (modal.ShowDialog() == true && modal.ResolvedChoices.Count > 0)
                    {
                        RunLintButton.IsButtonEnabledValue = false;
                        IFileSystemProvider? fs2 = null;
                        try
                        {
                            var resolveLog = new LogWriter("=== Conflict Resolution ===");
                            string targetPath = _isSftp ? SftpRootPath.TextValue : _lastScannedDirectory;

                            if (_isSftp)
                            {
                                if (!int.TryParse(SftpPort.Value, out int p)) p = 22;
                                fs2 = new SftpFileSystemProvider(SftpHost.TextValue, p,
                                    SftpUsername.TextValue, SftpPassword.Password);
                            }
                            else
                            {
                                fs2 = new LocalFileSystemProvider();
                            }

                            int q = await LinterAutoFixer.ResolveConflictsAsync(
                                fs2, targetPath,
                                modal.ResolvedChoices,
                                result.InstalledResources,
                                resolveLog);

                            MessageBox.Show(
                                $"Conflict Resolution complete: {q} script folder(s) quarantined.\n" +
                                "Folders renamed to .disabled_* and commented out in server.cfg.\n" +
                                "Re-run the Linter to confirm a clean state.",
                                "TGToolKit — Resolved",
                                MessageBoxButton.OK, MessageBoxImage.Information);
                        }
                        catch (Exception rex)
                        {
                            MessageBox.Show($"Resolution error:\n{rex.Message}",
                                "Conflict Resolver", MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                        finally
                        {
                            fs2?.Disconnect();
                            RunLintButton.IsButtonEnabledValue = true;
                        }
                    }
                }

                // Reveal Fix All button only in local mode when deprecated manifests were found
                FixAllButton.Visibility =
                    (!_isSftp && result.DeprecatedManifestPaths.Count > 0)
                        ? Visibility.Visible
                        : Visibility.Collapsed;

                // Auto-Wire is available after any local scan (it analyses the full ecosystem)
                AutoWireButton.Visibility = !_isSftp ? Visibility.Visible : Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Linter error:\n{ex.Message}", "Server Linter Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                RunLintButton.IsButtonEnabledValue = true;
            }
        }

        private void FixAllButton_Click(object sender, RoutedEventArgs e)
        {
            if (_lastResult == null || _lastResult.DeprecatedManifestPaths.Count == 0)
            {
                MessageBox.Show("No deprecated manifests to fix.", "Auto-Fix",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var confirm = MessageBox.Show(
                $"This will convert {_lastResult.DeprecatedManifestPaths.Count} __resource.lua file(s) to fxmanifest.lua.\n\n" +
                "Each file will be rewritten in-place. A backup is NOT created. Continue?",
                "TGToolKit — Auto-Fix Manifests",
                MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes) return;

            var log      = new LogWriter("=== Auto-Fix started ===");
            int fixed_   = LinterAutoFixer.FixDeprecatedManifests(_lastResult, log);

            // Update warning count display
            int remaining = _lastResult.DeprecatedManifestPaths.Count;
            ResultWarnings.Text = _lastResult.Warnings.Count.ToString();

            MessageBox.Show(
                $"Auto-Fix complete: {fixed_} manifest(s) converted to fxmanifest.lua.",
                "TGToolKit — Auto-Fix",
                MessageBoxButton.OK, MessageBoxImage.Information);

            // Hide the button if nothing is left to fix
            if (remaining == 0)
                FixAllButton.Visibility = Visibility.Collapsed;
        }
        private async void AutoWireButton_Click(object sender, RoutedEventArgs e)
        {
            if (_lastResult == null)
            {
                MessageBox.Show("Run a scan first.", "Auto-Wire",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Local mode requires a scanned directory; SFTP uses the configured root path
            string targetPath = _isSftp ? SftpRootPath.TextValue : _lastScannedDirectory;
            if (string.IsNullOrWhiteSpace(targetPath))
            {
                MessageBox.Show("No target path available. Run a scan first.", "Auto-Wire",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string modeLabel = _isSftp ? $"SFTP ({SftpHost.TextValue})" : "Local Disk";

            var confirm = MessageBox.Show(
                $"TGToolKit will intelligently re-wire configuration files across your server.\n\n" +
                $"  • Mode: {modeLabel}\n" +
                $"  • Detected Ecosystem: {_lastResult.InstalledResources.Count} resources\n" +
                "  • server.cfg convar injection (pma-voice, oxmysql)\n" +
                "  • Universal config re-routing (Framework, Inventory, Target, Phone, UI)\n" +
                "  • Qbox-specific native code path activation\n" +
                "  • ox_lib fxmanifest injection for missing declarations\n\n" +
                "⚠️ All modified files receive a .tg_backup before changes are applied.\n" +
                "Continue?",
                "TGToolKit — Auto-Wire Config",
                MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes) return;

            AutoWireButton.IsEnabled           = false;
            RunLintButton.IsButtonEnabledValue = false;

            IFileSystemProvider? fs = null;
            try
            {
                var log = new LogWriter("=== User Initiated Auto-Wire ===");

                // Construct the right provider for this mode
                if (_isSftp)
                {
                    if (!int.TryParse(SftpPort.Value, out int port)) port = 22;
                    fs = new SftpFileSystemProvider(
                        SftpHost.TextValue,
                        port,
                        SftpUsername.TextValue,
                        SftpPassword.Password);
                }
                else
                {
                    fs = new LocalFileSystemProvider();
                }

                int fixes = await LinterAutoFixer.ApplySmartFixesAsync(
                    fs,
                    targetPath,
                    _lastResult.InstalledResources,
                    log);

                MessageBox.Show(
                    $"Auto-Wire complete: {fixes} fix(es) applied via {modeLabel}.\n\n" +
                    "Backups (.tg_backup) created beside every modified file.\n" +
                    "Re-run the Linter to verify the changes.",
                    "TGToolKit — Auto-Wire Complete",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Auto-Wire error:\n{ex.Message}", "Auto-Wire Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                fs?.Disconnect();
                AutoWireButton.IsEnabled           = true;
                RunLintButton.IsButtonEnabledValue = true;
            }
        }
    }
}
