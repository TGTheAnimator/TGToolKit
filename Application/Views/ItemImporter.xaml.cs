using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Microsoft.Win32;
using ToolKitV.Models;
using ToolKitV.Models.Providers;
using WinForms = System.Windows.Forms;

namespace ToolKitV.Views
{
    public class ImageStatusConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string path = value as string ?? "";
            return string.IsNullOrEmpty(path) ? "[MISSING]" : "[FOUND]";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    public class ImageColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string path = value as string ?? "";
            return string.IsNullOrEmpty(path) ? Brushes.Red : new SolidColorBrush(Color.FromRgb(0x4C, 0xFF, 0x70));
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    public partial class ItemImporter : UserControl
    {
        private ObservableCollection<HarvestedItem> _harvestedItems = new();
        private ServerLinter? _serverLinterRef; // A reference to grab active SFTP credentials

        public ItemImporter()
        {
            InitializeComponent();
            ItemGrid.ItemsSource = _harvestedItems;
        }

        // We inject the active Linter so we don't have to duplicate the SFTP login box
        public void RegisterLinterReference(ServerLinter linter)
        {
            _serverLinterRef = linter;
        }

        [System.Runtime.Versioning.SupportedOSPlatform("windows6.1")]
        private void btnScan_Click(object sender, RoutedEventArgs e)
        {
            using var fbd = new WinForms.FolderBrowserDialog
            {
                Description = "Select the script folder to scan for items and images",
                UseDescriptionForTitle = true
            };

            if (fbd.ShowDialog() == WinForms.DialogResult.OK)
            {
                string path = fbd.SelectedPath;
                try
                {
                    var items = ItemHarvester.HarvestFromDirectory(path);
                    _harvestedItems.Clear();
                    foreach(var item in items)
                    {
                        _harvestedItems.Add(item);
                    }

                    if (_harvestedItems.Count > 0)
                    {
                        btnInject.IsEnabled = true;
                        MessageBox.Show($"Successfully harvested {_harvestedItems.Count} items.", "Scan Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        btnInject.IsEnabled = false;
                        MessageBox.Show("No valid Lua item definitions found in this directory.", "Scan Complete", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error scanning directory:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void DuplicateRow_Click(object sender, RoutedEventArgs e)
        {
            if (ItemGrid.SelectedItem is HarvestedItem selectedItem)
            {
                var copy = new HarvestedItem
                {
                    SpawnCode = selectedItem.SpawnCode + "_copy",
                    Label = selectedItem.Label + " (Copy)",
                    Weight = selectedItem.Weight,
                    ImageFileName = selectedItem.ImageFileName,
                    LocalImagePath = selectedItem.LocalImagePath,
                    // Duplicate the raw snippet so it can be edited to create the variation
                    RawLuaSnippet = selectedItem.RawLuaSnippet.Replace($"['{selectedItem.SpawnCode}']", $"['{selectedItem.SpawnCode}_copy']")
                };
                
                _harvestedItems.Add(copy);
            }
        }

        private async void btnInject_Click(object sender, RoutedEventArgs e)
        {
            if (_harvestedItems.Count == 0) return;
            if (_serverLinterRef == null)
            {
                MessageBox.Show("Application routing error: ServerLinter reference missing.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var confirm = MessageBox.Show(
                $"This will inject {_harvestedItems.Count} items and upload any valid images to the server.\n\n" +
                "A .tg_backup file will be created for the items.lua before modification.\n" +
                "Proceed?",
                "Asset & Item Importer", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes) return;

            btnInject.IsEnabled = false;

            IFileSystemProvider? fs = null;
            try
            {
                // Grab the active provider from the Linter
                if (_serverLinterRef.IsSftpMode())
                {
                    fs = _serverLinterRef.GetConfiguredProvider();
                }
                else
                {
                    fs = new LocalFileSystemProvider();
                }

                string targetInventory = (cbTargetInventory.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "";
                string rootPath = _serverLinterRef.GetRootPath();
                
                if (string.IsNullOrWhiteSpace(rootPath))
                {
                    MessageBox.Show("Please run a scan in the Server Linter first to establish the server root path.", "Path Missing", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var audit = new AuditLogger();
                await InventoryInjector.InjectItemsAsync(fs, rootPath, targetInventory, _harvestedItems.ToList(), audit);

                string auditFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tgtoolkit_item_audit.txt");
                File.WriteAllText(auditFile, audit.GenerateReport());

                MessageBox.Show($"Injection complete! The items.lua was backed up and appended.\n\nAudit log saved to:\n{auditFile}", "Injection Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch(Exception ex)
            {
                MessageBox.Show($"Error during injection:\n{ex.Message}", "Injection Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                fs?.Disconnect();
                btnInject.IsEnabled = true;
            }
        }
    }
}
