using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ToolKitV.Models;
using ToolKitV.Models.Providers;
using System.Text.RegularExpressions;
using System.Linq;

namespace ToolKitV.Views
{
    public partial class ServerLinter : UserControl
    {
        public class IssueViewModel
        {
            public string ResourceName { get; set; } = string.Empty;
            public string Message      { get; set; } = string.Empty;
            public Brush  SeverityColor { get; set; } = Brushes.White;
            public string Signature    { get; set; } = string.Empty;
            public bool   IsFixable    { get; set; } = false;
            public Models.ServerLinter.LinterWarning RawWarning { get; set; } = null!;
        }

        private bool _isSftp = false;
        private Models.ServerLinter.LinterResult? _lastResult;
        private string _lastScannedDirectory = string.Empty;
        private List<IntegrationRecipe> _applicableRecipes = new();
        private LinterIgnoreManager? _ignoreManager;
        public ObservableCollection<IssueViewModel> _observableIssues = new();

        /// <summary>
        /// Sanitises the raw host string the user typed into the SFTP Host field.
        /// Handles all common copy-paste formats from hosting panels:
        ///   "sftp://hostname:2022"  → ("hostname", 2022)
        ///   "hostname:2022"         → ("hostname", 2022)
        ///   "hostname"              → ("hostname", fallbackPort)
        /// </summary>
        private static (string host, int port) ParseSftpHost(string raw, int fallbackPort)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return (string.Empty, fallbackPort);

            // Strip any URI scheme prefix (sftp://, ssh://, ftp://, etc.)
            string cleaned = raw.Trim();
            foreach (var scheme in new[] { "sftp://", "ssh://", "ftp://", "http://", "https://" })
            {
                if (cleaned.StartsWith(scheme, StringComparison.OrdinalIgnoreCase))
                {
                    cleaned = cleaned[scheme.Length..];
                    break;
                }
            }

            // Strip trailing path (e.g. "hostname:2022/home")
            int slashIdx = cleaned.IndexOf('/');
            if (slashIdx >= 0) cleaned = cleaned[..slashIdx];

            // Extract embedded port  ("hostname:2022")
            int colonIdx = cleaned.LastIndexOf(':');
            if (colonIdx >= 0 && int.TryParse(cleaned[(colonIdx + 1)..], out int embeddedPort))
                return (cleaned[..colonIdx].Trim(), embeddedPort);

            return (cleaned.Trim(), fallbackPort);
        }

        public string GetSftpPassword() => SftpPassword?.Password ?? string.Empty;
        public bool IsSftpMode() => _isSftp;
        public string GetLocalFolder() => LocalFolder?.Path ?? string.Empty;

        public IFileSystemProvider GetConfiguredProvider()
        {
            if (_isSftp)
            {
                int fallback = int.TryParse(SftpPort.Value, out int pf) ? pf : 22;
                var (h, p) = ParseSftpHost(SftpHost.TextValue, fallback);
                return new SftpFileSystemProvider(h, p, SftpUsername.TextValue, SftpPassword.Password);
            }
            return new LocalFileSystemProvider();
        }

        public string GetRootPath()
        {
            return _isSftp ? SftpRootPath.TextValue : _lastScannedDirectory;
        }

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
            ScanningCard.Visibility            = Visibility.Visible;
            ScanStatusText.Text                = _isSftp ? "Connecting to SFTP server…" : "Initialising scan…";
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
                    _lastScannedDirectory = rootPath;
                    ScanStatusText.Text   = "Scanning local resources…";
                }
                else
                {
                    int fallbackLint = int.TryParse(SftpPort.Value, out int pfL) ? pfL : 22;
                    var (hLint, pLint) = ParseSftpHost(SftpHost.TextValue, fallbackLint);

                    ScanStatusText.Text = "Connecting to SFTP and downloading manifests…";
                    provider = new SftpManifestProvider(
                        hLint, pLint,
                        SftpUsername.TextValue,
                        SftpPassword.Password);

                    rootPath              = SftpRootPath.TextValue;
                    _lastScannedDirectory = rootPath;  // store so Auto-Wire has a valid target
                    ScanStatusText.Text   = "Scanning remote resources…";
                }

                var progress = new Progress<int>(n =>
                    Dispatcher.Invoke(() =>
                        ScanStatusText.Text = $"Analysing… {n} resource{(n == 1 ? "" : "s")} scanned"));

                _ignoreManager = new LinterIgnoreManager(_lastScannedDirectory);
                var log = new LogWriter("=== Server Linter started ===");

                Models.ServerLinter.LinterResult result =
                    await Models.ServerLinter.RunLinterAsync(provider, rootPath, progress, log);

                ScanningCard.Visibility = Visibility.Collapsed;

                ResultScanned.Text  = result.ResourcesScanned.ToString();
                ResultIssues.Text   = result.ResourcesWithIssues.ToString();
                ResultWarnings.Text = result.Warnings.Count.ToString();

                if (result.Warnings.Count == 0)
                {
                    CleanCard.Visibility = Visibility.Visible;
                }
                else
                {
                    _observableIssues.Clear();
                    foreach (var w in result.Warnings)
                    {
                        // Filter ignores
                        if (_ignoreManager != null && _ignoreManager.IsIgnored(w.ResourceName, w.Message))
                            continue;

                        var color = w.Severity switch
                        {
                            Models.ServerLinter.Severity.Critical => new SolidColorBrush(Color.FromRgb(0xFF, 0x55, 0x55)),
                            Models.ServerLinter.Severity.Warning  => new SolidColorBrush(Color.FromRgb(0xFF, 0xB3, 0x47)),
                            _                                      => new SolidColorBrush(Color.FromRgb(0x55, 0xAA, 0xFF))
                        };

                        _observableIssues.Add(new IssueViewModel
                        {
                            ResourceName  = w.ResourceName,
                            Message       = w.Message,
                            SeverityColor = color,
                            Signature     = w.Message,
                            IsFixable     = w.Message.Contains("fxmanifest.lua") || w.Message.Contains("Stream conflict"),
                            RawWarning    = w
                        });
                    }

                    if (_observableIssues.Count == 0)
                    {
                        CleanCard.Visibility = Visibility.Visible;
                    }
                    else
                    {
                        IssuesList.ItemsSource = _observableIssues;
                    }
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
                                int fallback2 = int.TryParse(SftpPort.Value, out int pf2) ? pf2 : 22;
                                var (h2, p2)  = ParseSftpHost(SftpHost.TextValue, fallback2);
                                fs2 = new SftpFileSystemProvider(h2, p2,
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

                // Fix All (local-only deprecated manifest converter)
                FixAllButton.Visibility =
                    (!_isSftp && result.DeprecatedManifestPaths.Count > 0)
                        ? Visibility.Visible
                        : Visibility.Collapsed;

                // Auto-Fix Manifests, Auto-Wire, and Restore available after ANY scan — local OR SFTP
                FixManifestsButton.Visibility         = Visibility.Visible;
                AutoWireButton.Visibility             = Visibility.Visible;
                RestoreBackupsButton.Visibility       = Visibility.Visible;
                RunIntegrationBridgeButton.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                ScanningCard.Visibility = Visibility.Collapsed;
                MessageBox.Show($"Linter error:\n{ex.Message}", "Server Linter Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                ScanningCard.Visibility            = Visibility.Collapsed;
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
        private async void FixManifestsButton_Click(object sender, RoutedEventArgs e)
        {
            string targetPath = _isSftp ? SftpRootPath.TextValue : _lastScannedDirectory;
            if (string.IsNullOrWhiteSpace(targetPath))
            {
                MessageBox.Show("No scan path available. Run a scan first.", "Auto-Fix Manifests",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string modeLabel = _isSftp ? $"SFTP ({SftpHost.TextValue})" : "Local Disk";

            var confirm = MessageBox.Show(
                $"TGToolKit will perform a two-sweep manifest fix across your server.\n\n" +
                $"  • Mode: {modeLabel}\n" +
                "  • Sweep 1 — Convert all __resource.lua → fxmanifest.lua\n" +
                "  • Sweep 2 — Inject missing fx_version / game headers\n\n" +
                "⚠️ All modified files receive a .tg_backup before changes are applied.\n" +
                "Continue?",
                "TGToolKit — Auto-Fix Manifests",
                MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes) return;

            FixManifestsButton.IsEnabled       = false;
            AutoWireButton.IsEnabled           = false;
            RunLintButton.IsButtonEnabledValue = false;

            IFileSystemProvider? fs = null;
            try
            {
                var log = new LogWriter("=== User Initiated Manifest Fix ===");

                if (_isSftp)
                {
                    int fallbackMf = int.TryParse(SftpPort.Value, out int pfMf) ? pfMf : 22;
                    var (hMf, pMf) = ParseSftpHost(SftpHost.TextValue, fallbackMf);
                    fs = new SftpFileSystemProvider(hMf, pMf,
                        SftpUsername.TextValue, SftpPassword.Password);
                }
                else
                {
                    fs = new LocalFileSystemProvider();
                }

                int fixesCount = await LinterAutoFixer.FixManifestErrorsAsync(fs, targetPath, log);

                MessageBox.Show(
                    $"Auto-Fix Manifests complete: {fixesCount} file(s) fixed via {modeLabel}.\n\n" +
                    "Backups (.tg_backup) created beside every modified file.\n" +
                    "Re-run the Linter to verify the changes.",
                    "TGToolKit — Auto-Fix Manifests Complete",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Auto-Fix Manifests error:\n{ex.Message}", "Manifest Fix Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                fs?.Disconnect();
                FixManifestsButton.IsEnabled       = true;
                AutoWireButton.IsEnabled           = true;
                RunLintButton.IsButtonEnabledValue = true;
            }
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
                    int fallbackAw = int.TryParse(SftpPort.Value, out int pfAw) ? pfAw : 22;
                    var (hAw, pAw) = ParseSftpHost(SftpHost.TextValue, fallbackAw);
                    fs = new SftpFileSystemProvider(hAw, pAw,
                        SftpUsername.TextValue, SftpPassword.Password);
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

        private async void RestoreBackupsButton_Click(object sender, RoutedEventArgs e)
        {
            string targetPath = _isSftp ? SftpRootPath.TextValue : _lastScannedDirectory;
            if (string.IsNullOrWhiteSpace(targetPath))
            {
                MessageBox.Show("No scan path available. Run a scan first.", "Restore Backups",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var confirm = MessageBox.Show(
                "This will restore ALL .tg_backup files found under the server root,\n" +
                "reverting every file that was modified by the Auto-Wirer or Conflict Resolver.\n\n" +
                "Continue?",
                "TGToolKit — Emergency Rollback",
                MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes) return;

            RestoreBackupsButton.IsEnabled     = false;
            AutoWireButton.IsEnabled           = false;
            RunLintButton.IsButtonEnabledValue = false;

            IFileSystemProvider? fs = null;
            try
            {
                var log = new LogWriter("=== Emergency Rollback ===");

                if (_isSftp)
                {
                    int fallbackRb = int.TryParse(SftpPort.Value, out int pfRb) ? pfRb : 22;
                    var (hRb, pRb) = ParseSftpHost(SftpHost.TextValue, fallbackRb);
                    fs = new SftpFileSystemProvider(hRb, pRb,
                        SftpUsername.TextValue, SftpPassword.Password);
                }
                else
                {
                    fs = new LocalFileSystemProvider();
                }

                int restored = await LinterAutoFixer.RestoreBackupsAsync(fs, targetPath, log);

                if (restored == 0)
                {
                    MessageBox.Show("No .tg_backup files found. Nothing to restore.",
                        "Restore Backups", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show(
                        $"Rollback complete: {restored} file(s) restored to their original state.\n" +
                        "Re-run the Linter to verify the server is back to its previous state.",
                        "TGToolKit — Rollback Complete",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Rollback error:\n{ex.Message}", "Restore Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                fs?.Disconnect();
                RestoreBackupsButton.IsEnabled     = true;
                AutoWireButton.IsEnabled           = true;
                RunLintButton.IsButtonEnabledValue = true;
            }
        }

        // ── Integration Bridge Logic ──────────────────────────────────────────

        private async void RunIntegrationBridgeButton_Click(object sender, RoutedEventArgs e)
        {
            if (_lastResult == null)
            {
                MessageBox.Show("Run a scan first.", "Integration Bridge",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                // Disable UI while scanning
                RunIntegrationBridgeButton.IsEnabled = false;
                ScanStatusText.Text = "Scanning workspace for recipe applicability...";
                ScanningCard.Visibility = Visibility.Visible;

                // Identify applicable recipes using the cached resources from the scan
                string recipesDir = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Recipes");
                var availableResources = new List<string>(_lastResult.InstalledResources);
                _applicableRecipes = RecipeManager.GetApplicableRecipes(recipesDir, availableResources);

                ScanningCard.Visibility = Visibility.Collapsed;

                if (_applicableRecipes.Count == 0)
                {
                    MessageBox.Show("No applicable integration recipes found for the resources in your workspace.", "Integration Bridge", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // Show the overlay
                RecipeList.ItemsSource = _applicableRecipes;
                IntegrationOverlay.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                ScanningCard.Visibility = Visibility.Collapsed;
                MessageBox.Show($"Failed to scan for recipes:\n{ex.Message}", "Integration Bridge Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                RunIntegrationBridgeButton.IsEnabled = true;
            }
        }

        private void CancelIntegrationButton_Click(object sender, RoutedEventArgs e)
        {
            IntegrationOverlay.Visibility = Visibility.Collapsed;
        }

        private async void ExecuteIntegrationButton_Click(object sender, RoutedEventArgs e)
        {
            IntegrationOverlay.Visibility = Visibility.Collapsed;
            
            ScanStatusText.Text = "Executing Integration Patches...";
            ScanningCard.Visibility = Visibility.Visible;
            
            IFileSystemProvider? provider = null;
            try
            {
                string targetPath = _isSftp ? SftpRootPath.TextValue : _lastScannedDirectory;

                if (_isSftp)
                {
                    int fallback = int.TryParse(SftpPort.Value, out int pf) ? pf : 22;
                    var (h, p) = ParseSftpHost(SftpHost.TextValue, fallback);
                    provider = new SftpFileSystemProvider(h, p, SftpUsername.TextValue, SftpPassword.Password);
                }
                else
                {
                    provider = new LocalFileSystemProvider();
                }
                
                var log = new LogWriter("=== Integration Bridge ===");
                await BridgeEngine.ApplyRecipesAsync(provider, targetPath, _applicableRecipes, log);
                
                string auditFile = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tgtoolkit_audit.txt");
                MessageBox.Show($"Integration Bridge complete! Patched {_applicableRecipes.Count} recipes.\n\nAn audit log has been saved to:\n{auditFile}", "Integration Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch(Exception ex)
            {
                MessageBox.Show($"Error during execution:\n{ex.Message}", "Execution Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                provider?.Disconnect();
                ScanningCard.Visibility = Visibility.Collapsed;
            }
        }
        private async void btnIgnore_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is IssueViewModel issue)
            {
                if (_ignoreManager != null)
                {
                    await _ignoreManager.IgnoreIssueAsync(issue.ResourceName, issue.Signature, global: false);
                    _observableIssues.Remove(issue);
                    if (_observableIssues.Count == 0)
                        CleanCard.Visibility = Visibility.Visible;
                }
            }
        }

        private async void btnIgnoreAll_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is IssueViewModel issue)
            {
                if (_ignoreManager != null)
                {
                    await _ignoreManager.IgnoreIssueAsync(issue.ResourceName, issue.Signature, global: true);
                    
                    var toRemove = _observableIssues.Where(i => i.Signature == issue.Signature).ToList();
                    foreach (var rm in toRemove)
                        _observableIssues.Remove(rm);

                    if (_observableIssues.Count == 0)
                        CleanCard.Visibility = Visibility.Visible;
                }
            }
        }

        private async void btnFix_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is IssueViewModel issue)
            {
                if (issue.Signature.Contains("Stream conflict"))
                {
                    var resources = issue.ResourceName.Split(',').Select(r => r.Trim()).ToList();
                    StreamConflictDesc.Text = issue.Message;
                    StreamConflictList.ItemsSource = resources;
                    StreamConflictOverlay.Visibility = Visibility.Visible;
                }
                else if (issue.Signature.Contains("fxmanifest.lua") || issue.Signature.Contains("__resource.lua") || issue.Signature.Contains("game declaration"))
                {
                    // For manifest issues, we run the dedicated auto-fixer pipeline
                    ScanStatusText.Text = "Running Auto-Fixer for Manifests...";
                    ScanningCard.Visibility = Visibility.Visible;
                    
                    IFileSystemProvider? provider = null;
                    try
                    {
                        string targetPath = _isSftp ? SftpRootPath.TextValue : _lastScannedDirectory;

                        if (_isSftp)
                        {
                            int fallback = int.TryParse(SftpPort.Value, out int pf) ? pf : 22;
                            var (h, p) = ParseSftpHost(SftpHost.TextValue, fallback);
                            provider = new SftpFileSystemProvider(h, p, SftpUsername.TextValue, SftpPassword.Password);
                        }
                        else
                        {
                            provider = new LocalFileSystemProvider();
                        }
                        
                        var log = new LogWriter("=== Targeted Auto-Fix ===");
                        await LinterAutoFixer.FixManifestErrorsAsync(provider, targetPath, log);
                        
                        MessageBox.Show("Manifest fixes applied successfully! Re-scanning...", "Fix Applied", MessageBoxButton.OK, MessageBoxImage.Information);
                        RunLintButton_Click(this, new RoutedEventArgs());
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Error applying fix: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        ScanningCard.Visibility = Visibility.Collapsed;
                    }
                    finally
                    {
                        provider?.Disconnect();
                    }
                }
            }
        }

        private void CancelStreamConflictButton_Click(object sender, RoutedEventArgs e)
        {
            StreamConflictOverlay.Visibility = Visibility.Collapsed;
        }

        private async void KeepStreamResourceButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string winningResource)
            {
                StreamConflictOverlay.Visibility = Visibility.Collapsed;
                
                string desc = StreamConflictDesc.Text;
                var match = Regex.Match(desc, @"'([^']+)'");
                if (!match.Success) return;
                
                string conflictingFile = match.Groups[1].Value;
                var allResources = StreamConflictList.ItemsSource as List<string>;
                if (allResources == null) return;

                var losingResources = allResources.Where(r => r != winningResource).ToList();

                IFileSystemProvider? provider = null;
                try
                {
                    string targetPath = _isSftp ? SftpRootPath.TextValue : _lastScannedDirectory;

                    if (_isSftp)
                    {
                        int fallback = int.TryParse(SftpPort.Value, out int pf) ? pf : 22;
                        var (h, p) = ParseSftpHost(SftpHost.TextValue, fallback);
                        provider = new SftpFileSystemProvider(h, p, SftpUsername.TextValue, SftpPassword.Password);
                    }
                    else
                    {
                        provider = new LocalFileSystemProvider();
                    }

                    foreach (var loser in losingResources)
                    {
                        string loserRoot = targetPath.TrimEnd('/', '\\') + "/" + loser;
                        if (!_isSftp) loserRoot = System.IO.Path.Combine(targetPath, loser);

                        var files = await provider.DiscoverFilesAsync(loserRoot, conflictingFile);
                        foreach (var f in files)
                        {
                            await provider.DeleteFileAsync(f);
                        }
                    }
                    
                    MessageBox.Show($"Conflict resolved! Kept '{winningResource}' and deleted '{conflictingFile}' from others.", "Resolved", MessageBoxButton.OK, MessageBoxImage.Information);

                    RunLintButton_Click(this, new RoutedEventArgs());
                }
                catch(Exception ex)
                {
                    MessageBox.Show($"Error resolving conflict:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    provider?.Disconnect();
                }
            }
        }
        private void OpenGlobalTranspilerButton_Click(object sender, RoutedEventArgs e)
        {
            var mainWindow = Application.Current.MainWindow as MainWindow;
            if (mainWindow != null)
            {
                // This will trigger the routing in MainWindow to switch to the GlobalTranspiler view
                mainWindow.SideMenu.InvokeNavigateTo("GlobalTranspiler");
            }
        }
    }
}
