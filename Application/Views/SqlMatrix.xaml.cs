using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using ToolKitV.Models;
using ToolKitV.Models.Providers;

namespace ToolKitV.Views
{
    public partial class SqlMatrix : UserControl
    {
        private List<SqlRequirement> _requirements = new();

        public SqlMatrix()
        {
            InitializeComponent();
            LoadConfigs();
        }

        private void LoadConfigs()
        {
            var dbCfg = DbConfig.Load();
            DbHost.TextValue    = dbCfg.Host;
            DbPort.TextValue    = dbCfg.Port.ToString();
            DbUser.TextValue    = dbCfg.Username;
            DbPassword.Password = dbCfg.Password;
            DbName.TextValue    = dbCfg.Database;

            // Load SFTP settings dynamically from LinterConfig for user reference
            UpdateSftpInfo();
        }

        public void UpdateSftpInfo()
        {
            try
            {
                var lintCfg = LinterConfig.Load();
                if (!string.IsNullOrWhiteSpace(lintCfg.Host))
                {
                    SftpInfoText.Text = $"SFTP Mode Connected:\nsftp://{lintCfg.Username}@{lintCfg.Host}:{lintCfg.Port}\nResources folder: {lintCfg.RootPath}";
                }
                else
                {
                    SftpInfoText.Text = "No active SFTP connection. Configure Server Linter credentials first.";
                }
            }
            catch
            {
                SftpInfoText.Text = "No active SFTP connection profile found.";
            }
        }

        private void SaveConfigs()
        {
            int port = 3306;
            int.TryParse(DbPort.TextValue, out port);

            var dbCfg = new DbConfig
            {
                Host = DbHost.TextValue,
                Port = port,
                Username = DbUser.TextValue,
                Password = DbPassword.Password,
                Database = DbName.TextValue
            };
            dbCfg.Save();
        }

        private void Mode_Changed(object sender, RoutedEventArgs e)
        {
            bool isSftp = ModeSftpBtn?.IsChecked == true;
            if (LocalFolderPanel != null)
                LocalFolderPanel.Visibility = isSftp ? Visibility.Collapsed : Visibility.Visible;
            if (SftpInfoPanel != null)
                SftpInfoPanel.Visibility = isSftp ? Visibility.Visible : Visibility.Collapsed;
        }

        private async void TestConnectionBtn_Click(object sender, RoutedEventArgs e)
        {
            TestConnectionBtn.IsButtonEnabledValue = false;
            try
            {
                SaveConfigs();

                int port = 3306;
                int.TryParse(DbPort.TextValue, out port);
                var db = new DatabaseProvider(DbHost.TextValue, port, DbUser.TextValue, DbPassword.Password, DbName.TextValue);

                await Task.Run(async () => await db.TestConnectionAsync());
                MessageBox.Show("Successfully connected to live database!", "TGToolKit — DB Status Online", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Database connection failed:\n\n{ex.Message}", "Database Connection Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                TestConnectionBtn.IsButtonEnabledValue = true;
            }
        }

        private async void ScanBtn_Click(object sender, RoutedEventArgs e)
        {
            // Reset state
            ScanBtn.IsButtonEnabledValue = false;
            ProgressCard.Visibility      = Visibility.Visible;
            RemediationCard.Visibility   = Visibility.Collapsed;
            MatrixGrid.ItemsSource        = null;
            _requirements.Clear();

            try
            {
                SaveConfigs();

                // 1. Establish database connection provider
                int dbPortNum = 3306;
                int.TryParse(DbPort.TextValue, out dbPortNum);
                var db = new DatabaseProvider(DbHost.TextValue, dbPortNum, DbUser.TextValue, DbPassword.Password, DbName.TextValue);

                // 2. Establish filesystem provider
                IFileSystemProvider fs;
                string rootPath;

                if (ModeLocalBtn.IsChecked == true)
                {
                    if (string.IsNullOrWhiteSpace(LocalFolder.Path))
                    {
                        MessageBox.Show("Please select a local server resources folder to scan.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                        ProgressCard.Visibility = Visibility.Collapsed;
                        ScanBtn.IsButtonEnabledValue = true;
                        return;
                    }
                    fs = new LocalFileSystemProvider();
                    rootPath = LocalFolder.Path;
                }
                else
                {
                    var lintCfg = LinterConfig.Load();
                    if (string.IsNullOrWhiteSpace(lintCfg.Host) || string.IsNullOrWhiteSpace(lintCfg.Username))
                    {
                        MessageBox.Show("Please configure valid SFTP credentials inside the Server Linter panel before scanning remotely.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                        ProgressCard.Visibility = Visibility.Collapsed;
                        ScanBtn.IsButtonEnabledValue = true;
                        return;
                    }

                    string sftpPassword = string.Empty;
                    if (Application.Current.MainWindow is MainWindow mainWin)
                    {
                        sftpPassword = mainWin.GetSftpPassword();
                    }

                    if (string.IsNullOrEmpty(sftpPassword))
                    {
                        var prompt = new PasswordPromptWindow("SFTP Password Required", "Please enter your SFTP password to scan remote schemas:")
                        {
                            Owner = Application.Current.MainWindow
                        };

                        if (prompt.ShowDialog() == true)
                        {
                            sftpPassword = prompt.Password;
                        }
                        else
                        {
                            ProgressCard.Visibility = Visibility.Collapsed;
                            ScanBtn.IsButtonEnabledValue = true;
                            return;
                        }
                    }

                    fs = new SftpFileSystemProvider(lintCfg.Host, lintCfg.Port, lintCfg.Username, sftpPassword);
                    rootPath = lintCfg.RootPath;
                }

                // 3. Scan directories asynchronously
                var progress = new Progress<string>(s =>
                    Dispatcher.Invoke(() => StatusLabel.Text = s));

                List<SqlRequirement>? result = null;
                Exception? scanEx = null;

                await Task.Run(async () =>
                {
                    try
                    {
                        result = await SqlMatrixEngine.ScanAndCompareAsync(fs, rootPath, db, progress);
                    }
                    catch (Exception ex)
                    {
                        scanEx = ex;
                    }
                    finally
                    {
                        fs.Disconnect();
                    }
                });

                ProgressCard.Visibility = Visibility.Collapsed;

                if (scanEx != null)
                {
                    throw scanEx;
                }

                if (result != null)
                {
                    _requirements = result;
                    MatrixGrid.ItemsSource = _requirements;

                    int missingCount = _requirements.Count(r => !r.ExistsInLiveDb);
                    if (missingCount > 0)
                    {
                        RemediationTitleText.Text = $"⚠ Schema misalignment: {missingCount} table{(missingCount == 1 ? "" : "s")} missing!";
                        RemediationDescText.Text  = "Click 'Deploy Missing Schema' to automatically create the required tables in the database.";
                        RemediationCard.Visibility = Visibility.Visible;
                    }
                    else
                    {
                        MessageBox.Show("All parsed SQL table requirements are fully aligned and active in your database!", "TGToolKit — Schemas Synced", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
            }
            catch (Exception ex)
            {
                ProgressCard.Visibility = Visibility.Collapsed;
                MessageBox.Show($"Scan failed:\n\n{ex.Message}", "SQL Matrix Scan Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                ProgressCard.Visibility      = Visibility.Collapsed;
                ScanBtn.IsButtonEnabledValue = true;
            }
        }

        private async void ExecuteBtn_Click(object sender, RoutedEventArgs e)
        {
            ExecuteBtn.IsEnabled       = false;
            ProgressCard.Visibility    = Visibility.Visible;
            StatusLabel.Text           = "Remediating database alignment...";

            var missing = _requirements.Where(r => !r.ExistsInLiveDb).ToList();
            if (missing.Count == 0)
            {
                ProgressCard.Visibility = Visibility.Collapsed;
                ExecuteBtn.IsEnabled    = true;
                return;
            }

            try
            {
                int port = 3306;
                int.TryParse(DbPort.TextValue, out port);
                var db = new DatabaseProvider(DbHost.TextValue, port, DbUser.TextValue, DbPassword.Password, DbName.TextValue);

                int fixedCount = 0;
                Exception? runEx = null;

                await Task.Run(async () =>
                {
                    try
                    {
                        foreach (var req in missing)
                        {
                            Dispatcher.Invoke(() => StatusLabel.Text = $"Creating table: {req.TableName}...");
                            await db.ExecuteSqlAsync(req.RawCreateStatement);
                            req.ExistsInLiveDb = true;
                            fixedCount++;
                        }
                    }
                    catch (Exception ex)
                    {
                        runEx = ex;
                    }
                });

                ProgressCard.Visibility = Visibility.Collapsed;
                ExecuteBtn.IsEnabled    = true;

                // Refresh DataGrid layout bindings
                MatrixGrid.Items.Refresh();

                if (runEx != null)
                {
                    MessageBox.Show($"Remediation hit an error:\n\n{runEx.Message}", "Schema Deployment Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                else
                {
                    RemediationCard.Visibility = Visibility.Collapsed;
                    MessageBox.Show($"Remediation complete: {fixedCount} required table(s) successfully created inside your database!", "TGToolKit — Database Aligned", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                ProgressCard.Visibility = Visibility.Collapsed;
                ExecuteBtn.IsEnabled    = true;
                MessageBox.Show($"Remediation failed:\n\n{ex.Message}", "Remediation Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
