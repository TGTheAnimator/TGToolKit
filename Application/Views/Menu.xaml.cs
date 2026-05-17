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
                    UpdateBanner.Text      = "⬆  UPDATING...";
                    UpdateBanner.IsEnabled = false;
                    await Updater.ApplyUpdateAsync(release);
                }
                else
                {
                    // User declined — hide until next launch
                    UpdateBanner.Visibility = Visibility.Collapsed;
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

        private void Economy_Click(object sender, MouseButtonEventArgs e)
        {
            if (_activeView == "Economy") return;
            SetActiveItem("Economy");
            NavigateTo?.Invoke("Economy");
        }

        private void AssetAnalyzer_Click(object sender, MouseButtonEventArgs e)
        {
            if (_activeView == "AssetAnalyzer") return;
            SetActiveItem("AssetAnalyzer");
            NavigateTo?.Invoke("AssetAnalyzer");
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

        private void YtdSplitter_Click(object sender, MouseButtonEventArgs e)
        {
            if (_activeView == "YtdSplitter") return;
            SetActiveItem("YtdSplitter");
            NavigateTo?.Invoke("YtdSplitter");
        }

        private void ServerLinter_Click(object sender, MouseButtonEventArgs e)
        {
            if (_activeView == "ServerLinter") return;
            SetActiveItem("ServerLinter");
            NavigateTo?.Invoke("ServerLinter");
        }

        private void SirenBuilder_Click(object sender, MouseButtonEventArgs e)
        {
            if (_activeView == "SirenBuilder") return;
            SetActiveItem("SirenBuilder");
            NavigateTo?.Invoke("SirenBuilder");
        }


        private void RecipeStudio_Click(object sender, MouseButtonEventArgs e)
        {
            if (_activeView == "RecipeStudio") return;
            SetActiveItem("RecipeStudio");
            NavigateTo?.Invoke("RecipeStudio");
        }

        private void ItemImporter_Click(object sender, MouseButtonEventArgs e)
        {
            if (_activeView == "ItemImporter") return;
            SetActiveItem("ItemImporter");
            NavigateTo?.Invoke("ItemImporter");
        }

        private void SqlMatrix_Click(object sender, MouseButtonEventArgs e)
        {
            if (_activeView == "SqlMatrix") return;
            SetActiveItem("SqlMatrix");
            NavigateTo?.Invoke("SqlMatrix");
        }

        private void GlobalTranspiler_Click(object sender, MouseButtonEventArgs e)
        {
            if (_activeView == "GlobalTranspiler") return;
            SetActiveItem("GlobalTranspiler");
            NavigateTo?.Invoke("GlobalTranspiler");
        }

        private void SnapshotManager_Click(object sender, MouseButtonEventArgs e)
        {
            if (_activeView == "SnapshotManager") return;
            SetActiveItem("SnapshotManager");
            NavigateTo?.Invoke("SnapshotManager");
        }

        public void InvokeNavigateTo(string view)
        {
            if (_activeView == view) return;
            SetActiveItem(view);
            NavigateTo?.Invoke(view);
        }

        // ── Visual state ──────────────────────────────────────────────────────

        private void SetActiveItem(string view)
        {
            _activeView = view;

            bool texActive           = view == "TextureOptimizer";
            bool vehiclesActive      = view == "VehicleTools";
            bool assetActive         = view == "AssetAnalyzer";
            bool clothingActive      = view == "ClothingTools";
            bool audioActive         = view == "AudioViewer";
            bool ytdSplitActive      = view == "YtdSplitter";
            bool serverLinterActive  = view == "ServerLinter";
            bool sirenBuilderActive  = view == "SirenBuilder";
            bool recipeStudioActive  = view == "RecipeStudio";
            bool itemImporterActive  = view == "ItemImporter";

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

            // Economy item
            bool economyActive = view == "Economy";
            EconomyBg.Visibility         = economyActive ? Visibility.Visible   : Visibility.Collapsed;
            EconomyInactiveBg.Visibility = economyActive ? Visibility.Collapsed : Visibility.Visible;
            EconomyStripe.Visibility     = economyActive ? Visibility.Visible   : Visibility.Collapsed;
            EconomyLabel.FontWeight      = economyActive ? FontWeights.Bold     : FontWeights.Normal;
            EconomyLabel.Foreground      = economyActive
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


            // Clothing Tools item
            ClothingToolsBg.Visibility         = clothingActive ? Visibility.Visible   : Visibility.Collapsed;
            ClothingToolsInactiveBg.Visibility = clothingActive ? Visibility.Collapsed : Visibility.Visible;
            ClothingToolsStripe.Visibility     = clothingActive ? Visibility.Visible   : Visibility.Collapsed;
            ClothingToolsLabel.FontWeight      = clothingActive ? FontWeights.Bold     : FontWeights.Normal;
            ClothingToolsLabel.Foreground      = clothingActive
                ? new SolidColorBrush(Colors.White)
                : new SolidColorBrush(Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF));

            // Audio Viewer item
            AudioViewerActiveBg.Visibility   = audioActive ? Visibility.Visible   : Visibility.Collapsed;
            AudioViewerInactiveBg.Visibility = audioActive ? Visibility.Collapsed : Visibility.Visible;
            AudioViewerStripe.Visibility     = audioActive ? Visibility.Visible   : Visibility.Collapsed;
            AudioViewerLabel.FontWeight      = audioActive ? FontWeights.Bold     : FontWeights.Normal;
            AudioViewerLabel.Foreground      = audioActive
                ? new SolidColorBrush(Colors.White)
                : new SolidColorBrush(Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF));

            // YTD Splitter item
            YtdSplitterBg.Visibility         = ytdSplitActive ? Visibility.Visible   : Visibility.Collapsed;
            YtdSplitterInactiveBg.Visibility = ytdSplitActive ? Visibility.Collapsed : Visibility.Visible;
            YtdSplitterStripe.Visibility     = ytdSplitActive ? Visibility.Visible   : Visibility.Collapsed;
            YtdSplitterLabel.FontWeight      = ytdSplitActive ? FontWeights.Bold     : FontWeights.Normal;
            YtdSplitterLabel.Foreground      = ytdSplitActive
                ? new SolidColorBrush(Colors.White)
                : new SolidColorBrush(Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF));

            // Server Linter item
            ServerLinterBg.Visibility         = serverLinterActive ? Visibility.Visible   : Visibility.Collapsed;
            ServerLinterInactiveBg.Visibility = serverLinterActive ? Visibility.Collapsed : Visibility.Visible;
            ServerLinterStripe.Visibility     = serverLinterActive ? Visibility.Visible   : Visibility.Collapsed;
            ServerLinterLabel.FontWeight      = serverLinterActive ? FontWeights.Bold     : FontWeights.Normal;
            ServerLinterLabel.Foreground      = serverLinterActive
                ? new SolidColorBrush(Colors.White)
                : new SolidColorBrush(Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF));

            // Recipe Studio item
            RecipeStudioBg.Visibility         = recipeStudioActive ? Visibility.Visible   : Visibility.Collapsed;
            RecipeStudioInactiveBg.Visibility = recipeStudioActive ? Visibility.Collapsed : Visibility.Visible;
            RecipeStudioStripe.Visibility     = recipeStudioActive ? Visibility.Visible   : Visibility.Collapsed;
            RecipeStudioLabel.FontWeight      = recipeStudioActive ? FontWeights.Bold     : FontWeights.Normal;
            RecipeStudioLabel.Foreground      = recipeStudioActive
                ? new SolidColorBrush(Colors.White)
                : new SolidColorBrush(Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF));

            // Asset Importer item
            ItemImporterBg.Visibility         = itemImporterActive ? Visibility.Visible   : Visibility.Collapsed;
            ItemImporterInactiveBg.Visibility = itemImporterActive ? Visibility.Collapsed : Visibility.Visible;
            ItemImporterStripe.Visibility     = itemImporterActive ? Visibility.Visible   : Visibility.Collapsed;
            ItemImporterLabel.FontWeight      = itemImporterActive ? FontWeights.Bold     : FontWeights.Normal;
            ItemImporterLabel.Foreground      = itemImporterActive
                ? new SolidColorBrush(Colors.White)
                : new SolidColorBrush(Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF));

            // SQL Matrix item
            bool sqlMatrixActive = view == "SqlMatrix";
            SqlMatrixBg.Visibility         = sqlMatrixActive ? Visibility.Visible   : Visibility.Collapsed;
            SqlMatrixInactiveBg.Visibility = sqlMatrixActive ? Visibility.Collapsed : Visibility.Visible;
            SqlMatrixStripe.Visibility     = sqlMatrixActive ? Visibility.Visible   : Visibility.Collapsed;
            SqlMatrixLabel.FontWeight      = sqlMatrixActive ? FontWeights.Bold     : FontWeights.Normal;
            SqlMatrixLabel.Foreground      = sqlMatrixActive
                ? new SolidColorBrush(Colors.White)
                : new SolidColorBrush(Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF));

            // Global Transpiler item
            bool globalTranspilerActive = view == "GlobalTranspiler";
            GlobalTranspilerBg.Visibility         = globalTranspilerActive ? Visibility.Visible   : Visibility.Collapsed;
            GlobalTranspilerInactiveBg.Visibility = globalTranspilerActive ? Visibility.Collapsed : Visibility.Visible;
            GlobalTranspilerStripe.Visibility     = globalTranspilerActive ? Visibility.Visible   : Visibility.Collapsed;
            GlobalTranspilerLabel.FontWeight      = globalTranspilerActive ? FontWeights.Bold     : FontWeights.Normal;
            GlobalTranspilerLabel.Foreground      = globalTranspilerActive
                ? new SolidColorBrush(Colors.White)
                : new SolidColorBrush(Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF));

            // Snapshot Manager item
            bool snapshotManagerActive = view == "SnapshotManager";
            SnapshotManagerBg.Visibility         = snapshotManagerActive ? Visibility.Visible   : Visibility.Collapsed;
            SnapshotManagerInactiveBg.Visibility = snapshotManagerActive ? Visibility.Collapsed : Visibility.Visible;
            SnapshotManagerStripe.Visibility     = snapshotManagerActive ? Visibility.Visible   : Visibility.Collapsed;
            SnapshotManagerLabel.FontWeight      = snapshotManagerActive ? FontWeights.Bold     : FontWeights.Normal;
            SnapshotManagerLabel.Foreground      = snapshotManagerActive
                ? new SolidColorBrush(Colors.White)
                : new SolidColorBrush(Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF));

            // Siren Builder item
            SirenBuilderBg.Visibility         = sirenBuilderActive ? Visibility.Visible   : Visibility.Collapsed;
            SirenBuilderInactiveBg.Visibility = sirenBuilderActive ? Visibility.Collapsed : Visibility.Visible;
            SirenBuilderStripe.Visibility     = sirenBuilderActive ? Visibility.Visible   : Visibility.Collapsed;
            SirenBuilderLabel.FontWeight      = sirenBuilderActive ? FontWeights.Bold     : FontWeights.Normal;
            SirenBuilderLabel.Foreground      = sirenBuilderActive
                ? new SolidColorBrush(Colors.White)
                : new SolidColorBrush(Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF));


        }
    }
}
