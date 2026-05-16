using System;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ToolKitV.Models;

namespace ToolKitV.Views
{
    public partial class AssetAnalyzer : UserControl
    {
        private string _resourcePath = string.Empty;

        public AssetAnalyzer()
        {
            InitializeComponent();
        }

        private void OnResourcePathChanged(object sender, PropertyChangedEventArgs e)
        {
            _resourcePath = ResourceFolder.Path;
            RunAuditButton.IsButtonEnabled = !string.IsNullOrWhiteSpace(_resourcePath) && Directory.Exists(_resourcePath);
        }

        private void UIElement_OnDrop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files != null && files.Length > 0 && Directory.Exists(files[0]))
                {
                    ResourceFolder.Path = files[0];
                }
            }
        }

        private async void RunAuditButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_resourcePath) || !Directory.Exists(_resourcePath)) return;

            RunAuditButton.IsButtonEnabled = false;
            RunAuditButton.Title = "Scanning...";

            ResetDisplay();

            AuditResult? result = null;

            await Task.Run(() =>
            {
                result = ResourceAudit.AuditResource(_resourcePath, (progress, current, total) =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        RunAuditButton.SetProgress(progress);
                        RunAuditButton.Title = $"Scanning {current}/{total}...";
                    });
                });
            });

            if (result != null)
            {
                Dispatcher.Invoke(() =>
                {
                    ResultStatus.Text = result.OverallStatus.ToString();
                    ResultStatus.Foreground = result.OverallStatus == DangerLevel.Critical ? new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0x55, 0x55)) :
                                              result.OverallStatus == DangerLevel.Warning ? new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0xA5, 0x00)) :
                                              new SolidColorBrush(Color.FromArgb(0xFF, 0x4C, 0xFF, 0x70));

                    ResultTotalMB.Text = $"{result.TotalEstimatedMB:F2} MB";
                    ResultTextureMB.Text = $"{result.TextureVirtualMB:F2} MB";
                    ResultModelMB.Text = $"{result.ModelVirtualMB:F2} MB";
                    ResultCollisionMB.Text = $"{result.CollisionVirtualMB:F2} MB";
                    ResultAudioMB.Text = $"{result.AudioDiskMB:F2} MB";

                    if (result.Recommendations.Count > 0)
                    {
                        RecommendationsList.ItemsSource = result.Recommendations;
                        RecommendationsList.Visibility = Visibility.Visible;
                        NoRecommendationsText.Visibility = Visibility.Collapsed;
                    }
                    else
                    {
                        RecommendationsList.ItemsSource = null;
                        RecommendationsList.Visibility = Visibility.Collapsed;
                        NoRecommendationsText.Visibility = Visibility.Visible;
                    }

                    if (result.OverallStatus == DangerLevel.Critical)
                    {
                        MessageBox.Show(
                            "This resource is CRITICAL and likely exceeds streaming limits.\nCheck the report in the folder for details.",
                            "TGToolKit — Critical Audit Result",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                    }
                    else
                    {
                         MessageBox.Show(
                            "Audit Complete!\nCheck 'resource_audit_report.txt' in the folder for details.",
                            "TGToolKit — Audit Complete",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
                    }
                });
            }

            RunAuditButton.Title = "Run Asset Audit";
            RunAuditButton.ResetProgress();
            RunAuditButton.IsButtonEnabled = true;
        }

        private void ResetDisplay()
        {
            ResultStatus.Text = "—";
            ResultStatus.Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0xE0, 0xFF, 0xFF));
            ResultTotalMB.Text = "—";
            ResultTextureMB.Text = "—";
            ResultModelMB.Text = "—";
            ResultCollisionMB.Text = "—";
            ResultAudioMB.Text = "—";
            RecommendationsList.ItemsSource = null;
            RecommendationsList.Visibility = Visibility.Collapsed;
            NoRecommendationsText.Visibility = Visibility.Collapsed;
        }
    }
}
