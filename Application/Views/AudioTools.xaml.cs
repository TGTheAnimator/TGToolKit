using System;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using ToolKitV.Models;

namespace ToolKitV.Views
{
    public partial class AudioTools : UserControl
    {
        private string _resourcePath = string.Empty;
        private bool _downsampleEnabled = true;

        public AudioTools()
        {
            InitializeComponent();
        }

        private void OnResourcePathChanged(object sender, PropertyChangedEventArgs e)
        {
            if (ResourceFolder == null) return;
            _resourcePath = ResourceFolder.Path;
            ValidateInputs();
        }

        private void OnSettingsChanged(object sender, PropertyChangedEventArgs e)
        {
            if (DownsampleAudio == null) return;
            _downsampleEnabled = DownsampleAudio.IsToogled;
            ValidateInputs();
        }

        private void ValidateInputs()
        {
            if (OptimizeButton == null) return;
            OptimizeButton.IsButtonEnabled = !string.IsNullOrWhiteSpace(_resourcePath) && Directory.Exists(_resourcePath) && _downsampleEnabled;
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

        private async void OptimizeButton_Click(object sender, RoutedEventArgs e)
        {
            if (!OptimizeButton.IsButtonEnabled) return;

            OptimizeButton.IsButtonEnabled = false;
            OptimizeButton.Title = "Optimizing...";
            ResultStatus.Text = "Running...";
            ResultStatus.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0xFF, 0xE0, 0xFF, 0xFF));

            var result = await Task.Run(() => AudioOptimizer.OptimizeFolder(_resourcePath, 24000));

            Dispatcher.Invoke(() =>
            {
                ResultFiles.Text = result.FilesProcessed.ToString();
                double mbReduced = result.BytesSaved / 1024.0 / 1024.0;
                ResultSize.Text = $"-{mbReduced:F2} MB";

                if (result.Success)
                {
                    ResultStatus.Text = "✓ Complete";
                    ResultStatus.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0xFF, 0x4C, 0xFF, 0x70));
                    MessageBox.Show($"Optimized {result.FilesProcessed} AWC files.\nSaved {mbReduced:F2} MB of VRAM!", "TGToolKit — Audio Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    ResultStatus.Text = "✗ Failed";
                    ResultStatus.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0xFF, 0xFF, 0x55, 0x55));
                    MessageBox.Show($"Error processing audio:\n\n{result.ErrorMessage}", "TGToolKit — Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            });

            OptimizeButton.Title = "Optimize Audio (.AWC)";
            ValidateInputs();
        }
    }
}
