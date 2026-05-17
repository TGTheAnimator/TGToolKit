using System;
using System.Windows;
using System.Windows.Controls;
using ToolKitV.Models;

namespace ToolKitV.Views
{
    public partial class SnapshotManager : UserControl
    {
        private readonly ServerLinter _linterRef;

        public SnapshotManager(ServerLinter linterRef)
        {
            InitializeComponent();
            _linterRef = linterRef;
            LoadSnapshots();
            UpdateWorkspaceLabel();
        }

        // ── Workspace label ────────────────────────────────────────────────────

        private void UpdateWorkspaceLabel()
        {
            string path = GetWorkspacePath();
            WorkspacePathLabel.Text = string.IsNullOrWhiteSpace(path)
                ? "No active workspace — connect via Server Linter first."
                : path;
        }

        private string GetWorkspacePath()
        {
            // Mirror the same resolution pattern used by GlobalTranspilerView
            if (_linterRef.IsSftpMode())
            {
                var cfg = Models.LinterConfig.Load();
                return string.IsNullOrWhiteSpace(cfg.RootPath) ? string.Empty : $"SFTP: {cfg.Host}{cfg.RootPath}";
            }
            return _linterRef.GetLocalFolder() ?? string.Empty;
        }

        private string GetRawWorkspacePath()
        {
            if (_linterRef.IsSftpMode())
            {
                var cfg = Models.LinterConfig.Load();
                return cfg.RootPath ?? string.Empty;
            }
            return _linterRef.GetLocalFolder() ?? string.Empty;
        }

        // ── Snapshot list ──────────────────────────────────────────────────────

        private void LoadSnapshots()
        {
            var snapshots = SnapshotEngine.ListSnapshots();

            if (snapshots.Length == 0)
            {
                SnapshotList.Visibility = Visibility.Collapsed;
                EmptyLabel.Visibility   = Visibility.Visible;
            }
            else
            {
                SnapshotList.Visibility = Visibility.Visible;
                EmptyLabel.Visibility   = Visibility.Collapsed;
                SnapshotList.ItemsSource = snapshots;
            }
        }

        private void SnapshotList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            RestoreButton.IsEnabled = SnapshotList.SelectedItem is SnapshotInfo;
            StatusLabel.Text = string.Empty;
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            LoadSnapshots();
            UpdateWorkspaceLabel();
            StatusLabel.Text = string.Empty;
        }

        // ── Restore ────────────────────────────────────────────────────────────

        private async void RestoreButton_Click(object sender, RoutedEventArgs e)
        {
            if (SnapshotList.SelectedItem is not SnapshotInfo snapshot) return;

            string targetPath = GetRawWorkspacePath();
            if (string.IsNullOrWhiteSpace(targetPath))
            {
                MessageBox.Show(
                    "No active workspace found. Please connect a workspace via the Server Linter tab first.",
                    "Workspace Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // SFTP-mode snapshots require a local path — guard against that
            if (_linterRef.IsSftpMode())
            {
                MessageBox.Show(
                    "Snapshot restore is only supported for local workspaces.\n\n" +
                    "For SFTP servers, manually extract the snapshot zip to your server's resources folder via SFTP client.",
                    "Local Mode Only", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var confirm = MessageBox.Show(
                $"⚠️  CRITICAL WARNING\n\n" +
                $"This will permanently OVERWRITE the current workspace at:\n{targetPath}\n\n" +
                $"with the snapshot from:\n{snapshot.FormattedDate}  ({snapshot.SizeMB:F2} MB)\n\n" +
                "This action cannot be undone. Are you absolutely sure?",
                "Confirm Rollback — Irreversible Action",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes) return;

            // Lock UI
            RestoreButton.IsEnabled = false;
            RefreshButton.IsEnabled = false;
            LoadingOverlay.Visibility = Visibility.Visible;
            StatusLabel.Text = string.Empty;

            try
            {
                var log = new LogWriter("=== Snapshot Restore ===");
                await SnapshotEngine.RestoreSnapshotAsync(snapshot.FilePath, targetPath, log);

                LoadingOverlay.Visibility = Visibility.Collapsed;
                MessageBox.Show(
                    "✅  Server workspace successfully restored to:\n" +
                    $"{snapshot.FormattedDate}\n\n" +
                    "You may now start your server.",
                    "Rollback Complete", MessageBoxButton.OK, MessageBoxImage.Information);

                StatusLabel.Text = $"✓  Restored to {snapshot.FormattedDate}";
                LoadSnapshots();
            }
            catch (Exception ex)
            {
                LoadingOverlay.Visibility = Visibility.Collapsed;
                MessageBox.Show(
                    $"Failed to restore snapshot:\n\n{ex.Message}",
                    "Restore Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                StatusLabel.Text = "❌  Restore failed — see above for details.";
            }
            finally
            {
                RestoreButton.IsEnabled = SnapshotList.SelectedItem != null;
                RefreshButton.IsEnabled = true;
            }
        }
    }
}
