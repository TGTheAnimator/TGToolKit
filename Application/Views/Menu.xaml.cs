using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ToolKitV.Models;
using System.Threading.Tasks;

namespace ToolKitV.Views
{
    public partial class Menu : UserControl
    {
        /// <summary>
        /// Fired when the user selects a tool. Payload is a view key:
        /// "TextureOptimizer" | "VehicleTools"
        /// </summary>
        public event Action<string>? NavigateTo;

        private string _activeView = "TextureOptimizer";

        public Menu()
        {
            InitializeComponent();
            SetActiveItem("TextureOptimizer");
            Loaded += Menu_Loaded;
        }

        private async void Menu_Loaded(object sender, RoutedEventArgs e)
        {
            // Small delay to let the app finish loading
            await Task.Delay(1000);
            
            var release = await Updater.CheckForUpdatesAsync();
            if (release != null)
            {
                UpdateBanner.Visibility = Visibility.Visible;
                UpdateBanner.Tag = release;
            }
        }

        private async void UpdateBanner_Click(object sender, MouseButtonEventArgs e)
        {
            if (UpdateBanner.Tag is Updater.ReleaseInfo release)
            {
                var result = MessageBox.Show(
                    $"A new version ({release.tag_name}) is available!\n\nWould you like to download and install it now?\n\n(The app will restart automatically)",
                    "TGToolKit — Update Available",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information);

                if (result == MessageBoxResult.Yes)
                {
                    UpdateBanner.Text = "• UPDATING...";
                    UpdateBanner.IsEnabled = false;
                    await Updater.ApplyUpdateAsync(release);
                }
            }
        }

        // ── Click handlers ────────────────────────────────────────────────────

        private void TextureOptimizer_Click(object sender, MouseButtonEventArgs e)
        {
            if (_activeView == "TextureOptimizer") return;
            SetActiveItem("TextureOptimizer");
            NavigateTo?.Invoke("TextureOptimizer");
        }

        private void Vehicles_Click(object sender, MouseButtonEventArgs e)
        {
            if (_activeView == "VehicleTools") return;
            SetActiveItem("VehicleTools");
            NavigateTo?.Invoke("VehicleTools");
        }

        private void AssetAnalyzer_Click(object sender, MouseButtonEventArgs e)
        {
            if (_activeView == "AssetAnalyzer") return;
            SetActiveItem("AssetAnalyzer");
            NavigateTo?.Invoke("AssetAnalyzer");
        }

        private void ModelViewer_Click(object sender, MouseButtonEventArgs e)
        {
            if (_activeView == "ModelViewer") return;
            SetActiveItem("ModelViewer");
            NavigateTo?.Invoke("ModelViewer");
        }

        private void ClothingTools_Click(object sender, MouseButtonEventArgs e)
        {
            if (_activeView == "ClothingTools") return;
            SetActiveItem("ClothingTools");
            NavigateTo?.Invoke("ClothingTools");
        }

        private void AudioViewer_Click(object sender, MouseButtonEventArgs e)
        {
            if (_activeView == "AudioViewer") return;
            SetActiveItem("AudioViewer");
            NavigateTo?.Invoke("AudioViewer");
        }

        // ── Visual state ──────────────────────────────────────────────────────

        private void SetActiveItem(string view)
        {
            _activeView = view;

            bool texActive      = view == "TextureOptimizer";
            bool vehiclesActive = view == "VehicleTools";
            bool assetActive    = view == "AssetAnalyzer";
            bool modelActive    = view == "ModelViewer";
            bool clothingActive = view == "ClothingTools";
            bool audioActive    = view == "AudioViewer";

            // Texture Optimizer item
            TextureOptimizerBg.Visibility         = texActive ? Visibility.Visible   : Visibility.Collapsed;
            TextureOptimizerInactiveBg.Visibility = texActive ? Visibility.Collapsed : Visibility.Visible;
            TextureOptimizerStripe.Visibility     = texActive ? Visibility.Visible   : Visibility.Collapsed;
            TextureOptimizerLabel.FontWeight      = texActive ? FontWeights.Bold     : FontWeights.Normal;
            TextureOptimizerLabel.Foreground      = texActive
                ? new SolidColorBrush(Colors.White)
                : new SolidColorBrush(Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF));

            // Vehicles item
            VehiclesBg.Visibility         = vehiclesActive ? Visibility.Visible   : Visibility.Collapsed;
            VehiclesInactiveBg.Visibility = vehiclesActive ? Visibility.Collapsed : Visibility.Visible;
            VehiclesStripe.Visibility     = vehiclesActive ? Visibility.Visible   : Visibility.Collapsed;
            VehiclesLabel.FontWeight      = vehiclesActive ? FontWeights.Bold     : FontWeights.Normal;
            VehiclesLabel.Foreground      = vehiclesActive
                ? new SolidColorBrush(Colors.White)
                : new SolidColorBrush(Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF));

            // Asset Analyzer item
            AssetAnalyzerBg.Visibility         = assetActive ? Visibility.Visible   : Visibility.Collapsed;
            AssetAnalyzerInactiveBg.Visibility = assetActive ? Visibility.Collapsed : Visibility.Visible;
            AssetAnalyzerStripe.Visibility     = assetActive ? Visibility.Visible   : Visibility.Collapsed;
            AssetAnalyzerLabel.FontWeight      = assetActive ? FontWeights.Bold     : FontWeights.Normal;
            AssetAnalyzerLabel.Foreground      = assetActive
                ? new SolidColorBrush(Colors.White)
                : new SolidColorBrush(Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF));

            // Model Viewer item
            ModelViewerBg.Visibility         = modelActive ? Visibility.Visible   : Visibility.Collapsed;
            ModelViewerInactiveBg.Visibility = modelActive ? Visibility.Collapsed : Visibility.Visible;
            ModelViewerStripe.Visibility     = modelActive ? Visibility.Visible   : Visibility.Collapsed;
            ModelViewerLabel.FontWeight      = modelActive ? FontWeights.Bold     : FontWeights.Normal;
            ModelViewerLabel.Foreground      = modelActive
                ? new SolidColorBrush(Colors.White)
                : new SolidColorBrush(Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF));

            // Clothing Tools item
            ClothingToolsBg.Visibility         = clothingActive ? Visibility.Visible   : Visibility.Collapsed;
            ClothingToolsInactiveBg.Visibility = clothingActive ? Visibility.Collapsed : Visibility.Visible;
            ClothingToolsStripe.Visibility     = clothingActive ? Visibility.Visible   : Visibility.Collapsed;
            ClothingToolsLabel.FontWeight      = clothingActive ? FontWeights.Bold     : FontWeights.Normal;
            ClothingToolsLabel.Foreground      = clothingActive
                ? new SolidColorBrush(Colors.White)
                : new SolidColorBrush(Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF));

            // Audio Viewer item
            AudioViewerActiveBg.Visibility         = audioActive ? Visibility.Visible   : Visibility.Collapsed;
            AudioViewerInactiveBg.Visibility = audioActive ? Visibility.Collapsed : Visibility.Visible;
            AudioViewerStripe.Visibility     = audioActive ? Visibility.Visible   : Visibility.Collapsed;
            AudioViewerLabel.FontWeight      = audioActive ? FontWeights.Bold     : FontWeights.Normal;
            AudioViewerLabel.Foreground      = audioActive
                ? new SolidColorBrush(Colors.White)
                : new SolidColorBrush(Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF));
        }
    }
}
