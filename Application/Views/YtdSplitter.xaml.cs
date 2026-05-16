using System;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using ToolKitV.Models;

namespace ToolKitV.Views
{
    public partial class YtdSplitter : UserControl
    {
        public YtdSplitter()
        {
            InitializeComponent();
        }

        private void OnPathChanged(object? sender, PropertyChangedEventArgs e)
        {
            SplitButton.IsButtonEnabledValue =
                !string.IsNullOrWhiteSpace(InputFolder.Path) &&
                !string.IsNullOrWhiteSpace(OutputFolder.Path);
        }

        private async void SplitButton_Click(object sender, RoutedEventArgs e)
        {
            string inputPath  = InputFolder.Path;
            string outputPath = OutputFolder.Path;

            if (string.IsNullOrWhiteSpace(inputPath) || string.IsNullOrWhiteSpace(outputPath))
                return;

            SplitButton.IsButtonEnabledValue = false;
            NextStepsCard.Visibility          = Visibility.Collapsed;

            ResultScanned.Text = "…";
            ResultSafe.Text    = "…";
            ResultSplit.Text   = "…";
            ResultChunks.Text  = "…";
            ResultMeta.Text    = "…";

            try
            {
                var progress = new Progress<int>();
                var log      = new LogWriter("=== YTD Splitter started ===");

                var result = await Models.YtdSplitter.SplitDirectoryAsync(inputPath, outputPath, progress, log);

                ResultScanned.Text = result.FilesScanned.ToString();
                ResultSafe.Text    = result.FilesSafe.ToString();
                ResultSplit.Text   = result.FilesSplit.ToString();
                ResultChunks.Text  = result.ChunksGenerated.ToString();
                ResultMeta.Text    = result.MetaGenerated ? "Yes ✓" : "No";

                if (result.FilesSplit > 0)
                    NextStepsCard.Visibility = Visibility.Visible;

                MessageBox.Show(
                    $"Split complete!\n\n" +
                    $"  Files scanned:    {result.FilesScanned}\n" +
                    $"  Files safe:       {result.FilesSafe}\n" +
                    $"  Files split:      {result.FilesSplit}\n" +
                    $"  Chunks generated: {result.ChunksGenerated}\n" +
                    (result.MetaGenerated ? "\nsplit_txd_relationships.meta has been generated." : ""),
                    "YTD Splitter — Done",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"An error occurred:\n{ex.Message}", "YTD Splitter Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                SplitButton.IsButtonEnabledValue = true;
            }
        }
    }
}
