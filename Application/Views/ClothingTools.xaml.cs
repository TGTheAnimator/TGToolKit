using System;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using ToolKitV.Models;

namespace ToolKitV.Views
{
    public partial class ClothingTools : UserControl
    {
        private string _resourcePath = string.Empty;
        private string _packName = "custom_clothing";
        private string _pedTarget = "mp_m_freemode_01";

        public ClothingTools()
        {
            InitializeComponent();
        }

        private void OnResourcePathChanged(object sender, PropertyChangedEventArgs e)
        {
            if (ResourceFolder == null) return;
            _resourcePath = ResourceFolder.Path;
            ValidateInputs();
        }

        private void OnPackSettingsChanged(object sender, PropertyChangedEventArgs e)
        {
            if (PackNameInput == null || PedTargetInput == null) return;
            _packName = PackNameInput.TextValue;
            _pedTarget = PedTargetInput.TextValue;
            ValidateInputs();
        }

        private void ValidateInputs()
        {
            if (GenerateButton == null) return;
            GenerateButton.IsButtonEnabled = !string.IsNullOrWhiteSpace(_resourcePath) && Directory.Exists(_resourcePath) && !string.IsNullOrWhiteSpace(_packName);
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

        private async void GenerateButton_Click(object sender, RoutedEventArgs e)
        {
            if (!GenerateButton.IsButtonEnabled) return;

            GenerateButton.IsButtonEnabled = false;
            GenerateButton.Title = "Generating...";
            ResultStatus.Text = "Running...";

            var result = await Task.Run(() => ClothingYmtGenerator.GeneratePack(_resourcePath, _packName, _pedTarget));

            Dispatcher.Invoke(() =>
            {
                ResultDrawables.Text = result.DrawablesFound.ToString();
                ResultTextures.Text = result.TexturesFound.ToString();

                if (result.Success)
                {
                    ResultStatus.Text = "✓ Complete";
                    ResultStatus.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0xFF, 0x4C, 0xFF, 0x70));
                    MessageBox.Show($"Generated Add-on Pack '{_packName}' successfully!\n\nCheck the folder for the fxmanifest.lua and YMT files.", "TGToolKit — Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    ResultStatus.Text = "✗ Failed";
                    ResultStatus.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0xFF, 0xFF, 0x55, 0x55));
                    MessageBox.Show($"Failed to generate clothing pack:\n\n{result.ErrorMessage}", "TGToolKit — Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            });

            GenerateButton.Title = "Generate Add-on Pack";
            ValidateInputs();
        }
    }
}
