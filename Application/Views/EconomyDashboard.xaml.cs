using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using ToolKitV.Models;
using ToolKitV.Models.Providers;

namespace ToolKitV.Views
{
    public partial class EconomyDashboard : UserControl
    {
        private List<EconomyItem> _masterItemList = new();
        private HashSet<EconomyItem> _modifiedItems = new();
        private IFileSystemProvider? _fs;
        private string _rootPath = string.Empty;

        // Custom progress report structure for real-time overlay bindings
        public class SyncProgress
        {
            public int Percentage { get; set; }
            public string Status { get; set; } = string.Empty;
            public string LogEntry { get; set; } = string.Empty;
        }

        public EconomyDashboard()
        {
            InitializeComponent();
            EconomyGrid.CellEditEnding += EconomyGrid_CellEditEnding;
        }

        public void InitializeDashboard(IFileSystemProvider fs, string rootPath)
        {
            _fs = fs;
            _rootPath = rootPath;
            _masterItemList.Clear();
            _modifiedItems.Clear();
            EconomyGrid.ItemsSource = null;
            StatusAlertPanel.Visibility = Visibility.Collapsed;

            // Trigger initial non-blocking scan on load
            TriggerScan();
        }

        private void TriggerScan()
        {
            ScanBtn_Click(this, new RoutedEventArgs());
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            string filter = SearchBox.Text.Trim();
            if (string.IsNullOrEmpty(filter))
            {
                EconomyGrid.ItemsSource = _masterItemList;
            }
            else
            {
                EconomyGrid.ItemsSource = _masterItemList
                    .Where(i => i.SpawnCode.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                                i.Label.Contains(filter, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }
        }

        private void EconomyGrid_CellEditEnding(object? sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.EditAction == DataGridEditAction.Commit)
            {
                if (e.Row.Item is EconomyItem editedItem)
                {
                    _modifiedItems.Add(editedItem);
                    StatusAlertPanel.Visibility = Visibility.Visible;
                }
            }
        }

        private async void ScanBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_fs == null || string.IsNullOrEmpty(_rootPath))
            {
                MessageBox.Show("Please connect to a local server folder or configure SFTP credentials in the Server Linter panel first.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            ActionOverlay.Visibility = Visibility.Visible;
            txtActionLog.Text = "";
            ActionProgressBar.Value = 0;
            txtOverlayTitle.Text = "Scanning Server Economy DNA...";

            IProgress<SyncProgress> progress = new Progress<SyncProgress>(p =>
            {
                ActionProgressBar.Value = p.Percentage;
                txtActionStatus.Text = p.Status;
                if (!string.IsNullOrEmpty(p.LogEntry))
                {
                    txtActionLog.Text += p.LogEntry + "\n";
                    ActionLogScroller.ScrollToEnd();
                }
            });

            string tempWorkspace = Path.Combine(Path.GetTempPath(), "TGToolKit_EconomyWorkspace");

            try
            {
                IFileSystemProvider activeFs = _fs;

                // For remote connections, prompt for password if needed
                if (activeFs is SftpFileSystemProvider)
                {
                    var lintCfg = LinterConfig.Load();
                    string sftpPassword = string.Empty;

                    if (Application.Current.MainWindow is MainWindow mainWin)
                    {
                        sftpPassword = mainWin.GetSftpPassword();
                    }

                    if (string.IsNullOrEmpty(sftpPassword))
                    {
                        var prompt = new PasswordPromptWindow("SFTP Password Required", "Please enter your SFTP password to scan server items:")
                        {
                            Owner = Application.Current.MainWindow
                        };

                        if (prompt.ShowDialog() == true)
                        {
                            sftpPassword = prompt.Password;
                        }
                        else
                        {
                            ActionOverlay.Visibility = Visibility.Collapsed;
                            return;
                        }
                    }

                    // Re-instantiate a fresh provider with valid password
                    activeFs = new SftpFileSystemProvider(lintCfg.Host, lintCfg.Port, lintCfg.Username, sftpPassword);
                }

                List<EconomyItem>? resultList = null;
                Exception? scanEx = null;

                await Task.Run(async () =>
                {
                    try
                    {
                        if (Directory.Exists(tempWorkspace))
                            Directory.Delete(tempWorkspace, true);
                        Directory.CreateDirectory(tempWorkspace);

                        // STEP 1: Fast Bulk Transfer Cloner to local workspace
                        progress.Report(new SyncProgress
                        {
                            Percentage = 10,
                            Status = "Cloning remote configuration folders to SSD workspace...",
                            LogEntry = "[SFTP] Initiating high-speed directory download..."
                        });

                        await activeFs.DownloadDirectoryAsync(_rootPath, tempWorkspace);

                        progress.Report(new SyncProgress
                        {
                            Percentage = 40,
                            Status = "Parsing Lua item definitions...",
                            LogEntry = "[SYS] Local clone completed. Analyzing tables..."
                        });

                        // STEP 2: SSD-Speed Local Processing
                        var localProvider = new LocalFileSystemProvider();
                        var localProgress = new Progress<string>(s => 
                            progress.Report(new SyncProgress { Percentage = 50, Status = s, LogEntry = $"[PARSE] {s}" }));

                        resultList = await EconomyEngine.BuildEconomyMatrixAsync(localProvider, tempWorkspace, localProgress);
                    }
                    catch (Exception ex)
                    {
                        scanEx = ex;
                    }
                    finally
                    {
                        // Clean up active SFTP if temporary
                        if (activeFs != _fs)
                        {
                            activeFs.Disconnect();
                        }

                        // Safely cleanup temp directories
                        try { if (Directory.Exists(tempWorkspace)) Directory.Delete(tempWorkspace, true); } catch { }
                    }
                });

                ActionOverlay.Visibility = Visibility.Collapsed;

                if (scanEx != null) throw scanEx;

                if (resultList != null)
                {
                    _masterItemList = resultList;
                    EconomyGrid.ItemsSource = _masterItemList;
                    _modifiedItems.Clear();
                    StatusAlertPanel.Visibility = Visibility.Collapsed;

                    MessageBox.Show($"Scanned {_masterItemList.Count} items successfully!\nAll configurations loaded into visual spreadsheet.", "Scan Completed", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                ActionOverlay.Visibility = Visibility.Collapsed;
                MessageBox.Show($"Economy scan failed:\n\n{ex.Message}", "Scan Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void btnSync_Click(object sender, RoutedEventArgs e)
        {
            if (!_modifiedItems.Any())
            {
                MessageBox.Show("No changes detected.", "Economy Sync", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            ActionOverlay.Visibility = Visibility.Visible;
            txtActionLog.Text = "";
            ActionProgressBar.Value = 0;
            txtOverlayTitle.Text = "Performing Economy Surgery...";

            IProgress<SyncProgress> progress = new Progress<SyncProgress>(p =>
            {
                ActionProgressBar.Value = p.Percentage;
                txtActionStatus.Text = p.Status;
                if (!string.IsNullOrEmpty(p.LogEntry))
                {
                    txtActionLog.Text += p.LogEntry + "\n";
                    ActionLogScroller.ScrollToEnd();
                }
            });

            string tempWorkspace = Path.Combine(Path.GetTempPath(), "TGToolKit_EconomySyncWorkspace");

            try
            {
                IFileSystemProvider activeFs = _fs!;

                if (activeFs is SftpFileSystemProvider)
                {
                    var lintCfg = LinterConfig.Load();
                    string sftpPassword = string.Empty;

                    if (Application.Current.MainWindow is MainWindow mainWin)
                    {
                        sftpPassword = mainWin.GetSftpPassword();
                    }

                    if (string.IsNullOrEmpty(sftpPassword))
                    {
                        var prompt = new PasswordPromptWindow("SFTP Password Required", "Please enter your SFTP password to apply economy updates:")
                        {
                            Owner = Application.Current.MainWindow
                        };

                        if (prompt.ShowDialog() == true)
                        {
                            sftpPassword = prompt.Password;
                        }
                        else
                        {
                            ActionOverlay.Visibility = Visibility.Collapsed;
                            return;
                        }
                    }

                    activeFs = new SftpFileSystemProvider(lintCfg.Host, lintCfg.Port, lintCfg.Username, sftpPassword);
                }

                int filesUpdated = 0;
                Exception? syncEx = null;

                await Task.Run(async () =>
                {
                    try
                    {
                        if (Directory.Exists(tempWorkspace))
                            Directory.Delete(tempWorkspace, true);
                        Directory.CreateDirectory(tempWorkspace);

                        progress.Report(new SyncProgress
                        {
                            Percentage = 15,
                            Status = "Downloading config files for delta modifications...",
                            LogEntry = "[SFTP] Downloading server directories to temp workspace..."
                        });

                        await activeFs.DownloadDirectoryAsync(_rootPath, tempWorkspace);

                        progress.Report(new SyncProgress
                        {
                            Percentage = 40,
                            Status = "Injecting modifications into Lua tables...",
                            LogEntry = "[SYS] Running local Regex Lua injectors..."
                        });

                        // Remap remote file paths to local temp workspace paths
                        var localModifiedItems = new List<EconomyItem>();
                        foreach (var item in _modifiedItems)
                        {
                            string localDefPath = string.Empty;
                            string localShopPath = string.Empty;

                            if (!string.IsNullOrEmpty(item.DefinitionFilePath))
                            {
                                string relativeDef = item.DefinitionFilePath.Replace(_rootPath, "").TrimStart('/', '\\');
                                localDefPath = Path.Combine(tempWorkspace, relativeDef);
                            }

                            if (!string.IsNullOrEmpty(item.ShopFilePath))
                            {
                                string relativeShop = item.ShopFilePath.Replace(_rootPath, "").TrimStart('/', '\\');
                                localShopPath = Path.Combine(tempWorkspace, relativeShop);
                            }

                            localModifiedItems.Add(new EconomyItem
                            {
                                SpawnCode = item.SpawnCode,
                                Label = item.Label,
                                Weight = item.Weight,
                                BuyPrice = item.BuyPrice,
                                SellPrice = item.SellPrice,
                                DefinitionFilePath = localDefPath,
                                ShopFilePath = localShopPath
                            });
                        }

                        var localProvider = new LocalFileSystemProvider();
                        var localProgress = new Progress<string>(s =>
                            progress.Report(new SyncProgress { Percentage = 60, Status = s, LogEntry = $"[INJECT] {s}" }));

                        // Perform surgical modifications on local workspace SSD
                        filesUpdated = await EconomyEngine.SyncEconomyDataAsync(localProvider, localModifiedItems, localProgress);

                        // Upload only the modified files back to the server
                        progress.Report(new SyncProgress
                        {
                            Percentage = 80,
                            Status = "Uploading modified files back to server...",
                            LogEntry = "[SFTP] Uploading modified configs..."
                        });

                        // Identify which local files actually changed and upload them
                        var filesToUpload = localModifiedItems.Select(i => i.DefinitionFilePath)
                            .Union(localModifiedItems.Select(i => i.ShopFilePath))
                            .Where(p => !string.IsNullOrEmpty(p) && File.Exists(p))
                            .Distinct()
                            .ToList();

                        int uploaded = 0;
                        foreach (var localFile in filesToUpload)
                        {
                            uploaded++;
                            string relative = localFile.Replace(tempWorkspace, "").Replace("\\", "/");
                            string remoteTarget = _rootPath.Replace("\\", "/").TrimEnd('/') + "/" + relative.TrimStart('/');

                            progress.Report(new SyncProgress
                            {
                                Percentage = 80 + (int)((float)uploaded / filesToUpload.Count * 15),
                                Status = $"Uploading file ({uploaded}/{filesToUpload.Count}): {Path.GetFileName(localFile)}...",
                                LogEntry = $"[SFTP] Uploading {relative}..."
                            });

                            // Pre-create backup on remote server
                            await activeFs.CreateBackupAsync(remoteTarget);
                            // Upload modified file
                            await activeFs.UploadFileAsync(localFile, remoteTarget);
                        }
                    }
                    catch (Exception ex)
                    {
                        syncEx = ex;
                    }
                    finally
                    {
                        if (activeFs != _fs)
                        {
                            activeFs.Disconnect();
                        }

                        try { if (Directory.Exists(tempWorkspace)) Directory.Delete(tempWorkspace, true); } catch { }
                    }
                });

                ActionOverlay.Visibility = Visibility.Collapsed;

                if (syncEx != null) throw syncEx;

                // Reset state on successful sync
                foreach (var item in _modifiedItems)
                {
                    item.IsModified = false;
                }
                _modifiedItems.Clear();
                StatusAlertPanel.Visibility = Visibility.Collapsed;

                // Refresh DataGrid layouts
                EconomyGrid.Items.Refresh();

                MessageBox.Show($"Synchronization Successful!\nUpdated {filesUpdated} files with pending changes. Backups (.tg_backup) were created.", "Economy Synced", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                ActionOverlay.Visibility = Visibility.Collapsed;
                MessageBox.Show($"Synchronization failed:\n\n{ex.Message}", "Sync Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
