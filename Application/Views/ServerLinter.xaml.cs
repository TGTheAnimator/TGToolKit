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
                }
                else
                {
                    MessageBox.Show(
                        "SFTP mode requires SSH.NET. Please ensure the package is installed.\n" +
                        "For now, use Local mode to scan a downloaded server folder.",
                        "SFTP Not Available",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    RunLintButton.IsButtonEnabledValue = true;
                    return;
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
    }
}
