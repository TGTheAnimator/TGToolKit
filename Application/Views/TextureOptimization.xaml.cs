using System;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using static ToolkitV.Models.TextureOptimization;

namespace ToolKitV.Views
{
    public partial class TextureOptimization : UserControl
    {
        public string MainPath         { get; set; } = "";
        public string BackupPath       { get; set; } = "";
        public string OptimizeSizeValue{ get; set; } = "4096";
        public bool   OnlyOverSizedToogled { get; set; } = false;
        public bool   DownSizeValue    { get; set; } = true;
        public bool   FormatOptimizeValue  { get; set; } = false;
        public bool   AutoDownscale4KValue { get; set; } = true;

        public TextureOptimization()
        {
            InitializeComponent();

            AnalyzeProgressHandler  = AnalyzeProgressValue;
            OptimizeProgressHandler = OptimizeProgressValue;
        }

        // ─── Stats display ───────────────────────────────────────────────────────

        private void UpdateData(StatsData data)
        {
            if (data.filesCount > 0)
            {
                Stats.FilesCount.Text    = data.filesCount.ToString();
                Stats.OversizedCount.Text= data.oversizedCount.ToString();
                Stats.VirtualSize.Text   = Math.Round(data.virtualSize,  2) + " MB";
                Stats.PhysicalSize.Text  = Math.Round(data.physicalSize, 2) + " MB";
            }
        }

        // ─── Progress callbacks ──────────────────────────────────────────────────

        public delegate void AnalyzeProgress(int progress);
        public Delegate AnalyzeProgressHandler;

        private void AnalyzeProgressValue(int progress)
        {
            Dispatcher.Invoke(() =>
            {
                AnalyzeButton.SetProgress(progress);
            });
        }

        public delegate void OptimizeProgress(ResultsData data, int progress);
        public Delegate OptimizeProgressHandler;

        private void OptimizeProgressValue(ResultsData data, int progress)
        {
            Dispatcher.Invoke(() =>
            {
                OptimizeButton.SetProgress(progress);

                if (data.filesOptimized > 0)
                {
                    Stats.OptimizedFiles.Text = data.filesOptimized.ToString();
                    Stats.OptimizedSize.Text  = Math.Round(data.optimizedSize, 2) + " MB";
                }
            });
        }

        // ─── Validation ──────────────────────────────────────────────────────────

        private bool CheckCanProceed()
            => MainPath != "" && System.IO.Directory.Exists(MainPath);

        // ─── Property-change handlers ────────────────────────────────────────────

        private void OnMainPathChanged(object sender, PropertyChangedEventArgs e)
        {
            MainPath = MainFolder.Path;
            bool ok = CheckCanProceed();
            OptimizeButton.IsButtonEnabled   = ok;
            AnalyzeButton.IsButtonEnabled    = ok;
            FixScriptRtButton.IsButtonEnabled = ok;
        }

        private void UIElement_OnDrop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files != null && files.Length > 0 && Directory.Exists(files[0]))
                {
                    MainFolder.Path = files[0];
                }
            }
        }

        private void OnBackupPathChanged(object sender, PropertyChangedEventArgs e)
            => BackupPath = BackupFolder.Path;

        private void OnOnlyOverSizedTexturesChanged(object sender, PropertyChangedEventArgs e)
            => OnlyOverSizedToogled = OnlyOverSized.IsToogled;

        private void OptimizeSize_PropertyChanged(object sender, PropertyChangedEventArgs e)
            => OptimizeSizeValue = OptimizeSize.Value;

        private void Downsize_PropertyChanged(object sender, PropertyChangedEventArgs e)
            => DownSizeValue = Downsize.IsToogled;

        private void FormatOptimize_PropertyChanged(object sender, PropertyChangedEventArgs e)
            => FormatOptimizeValue = FormatOptimize.IsToogled;

        private void AutoDownscale4K_PropertyChanged(object sender, PropertyChangedEventArgs e)
            => AutoDownscale4KValue = AutoDownscale4K.IsToogled;

        // ─── Button actions ──────────────────────────────────────────────────────

        private async void AnalyzeButton_Click(object sender, RoutedEventArgs e)
        {
            SetButtonsEnabled(false);
            AnalyzeButton.Title = "Scanning...";

            StatsData data = await Task.Run(() => GetStatsData(MainPath, AnalyzeProgressHandler));
            UpdateData(data);

            SetButtonsEnabled(true);
            AnalyzeButton.Title = "Analyze";
            AnalyzeButton.ResetProgress();
        }

        private async void OptimizeButton_Click(object sender, RoutedEventArgs e)
        {
            if (!DownSizeValue && !FormatOptimizeValue && !AutoDownscale4KValue)
            {
                MessageBox.Show(
                    "Please enable at least one optimization option:\n• Downsize (÷2)\n• Format Optimization (BC7/BC1/BC4)\n• Auto-Downscale 4K",
                    "TGToolKit — Nothing to do",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            SetButtonsEnabled(false);
            OptimizeButton.Title = "Optimizing...";

            // Gather pre-optimization stats.
            StatsData before = await Task.Run(() => GetStatsData(MainPath, null));
            UpdateData(before);

            // Run the optimization.
            await Task.Run(() => Optimize(
                MainPath, BackupPath, OptimizeSizeValue,
                OnlyOverSizedToogled, DownSizeValue, FormatOptimizeValue, AutoDownscale4KValue,
                OptimizeProgressHandler));

            // Gather post-optimization stats.
            StatsData after = await Task.Run(() => GetStatsData(MainPath, null));

            if (before.physicalSize > 0)
            {
                double saved   = before.physicalSize - after.physicalSize;
                double percent = 100.0 - (after.physicalSize * 100.0 / before.physicalSize);

                Stats.FilesSizeResult.Text  = Math.Round(after.physicalSize, 2) + " MB";
                Stats.OptimizedProcent.Text = Math.Round(percent, 2) + "%";
            }

            SetButtonsEnabled(true);
            OptimizeButton.Title = "Optimize";
            OptimizeButton.ResetProgress();
        }

        private void SetButtonsEnabled(bool enabled)
        {
            bool ok = enabled && CheckCanProceed();
            OptimizeButton.IsButtonEnabled    = ok;
            AnalyzeButton.IsButtonEnabled     = ok;
            FixScriptRtButton.IsButtonEnabled = ok;
        }

        private async void FixScriptRtButton_Click(object sender, RoutedEventArgs e)
        {
            SetButtonsEnabled(false);
            FixScriptRtButton.Title = "Scanning...";
            ScriptRtResultBorder.Visibility = System.Windows.Visibility.Collapsed;

            ScriptRtResultsData result = await Task.Run(() =>
                FixScriptRTs(MainPath, BackupPath, null!));

            Dispatcher.Invoke(() =>
            {
                ScriptRtScanned.Text  = result.ytdsScanned.ToString();
                ScriptRtFixed.Text    = result.ytdsFixed.ToString();
                ScriptRtTextures.Text = result.texturesFixed.ToString();
                ScriptRtResultBorder.Visibility = System.Windows.Visibility.Visible;
            });

            SetButtonsEnabled(true);
            FixScriptRtButton.Title = "Fix Script RT Crashes";
        }
    }
}