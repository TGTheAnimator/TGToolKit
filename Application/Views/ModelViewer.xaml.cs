using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using CodeWalker.GameFiles;

namespace ToolKitV.Views
{
    public partial class ModelViewer : UserControl
    {
        public ModelViewer()
        {
            InitializeComponent();
            ToggleViewportBtn.IsToogled = true; // Default to ON for the prototype demo
            LoadModelBtn.Click += LoadModelBtn_Click;
            LoadTextureBtn.Click += LoadTextureBtn_Click;
        }

        private void LoadTextureBtn_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = "GTA V Texture Dictionary (*.ytd)|*.ytd|All files (*.*)|*.*",
                Title = "Select a YTD Texture Dictionary"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                try
                {
                    byte[] data = File.ReadAllBytes(openFileDialog.FileName);
                    var ytd = new YtdFile();
                    ytd.Load(data);

                    if (ytd.TextureDict?.Textures?.data_items != null && ytd.TextureDict.Textures.data_items.Length > 0)
                    {
                        // Load ALL textures from the dict so the renderer can bind the right one
                        foreach (var tex in ytd.TextureDict.Textures.data_items)
                        {
                            Viewport3D.LoadTexture(tex);
                        }
                    }
                    else
                    {
                        MessageBox.Show("Could not find any textures in this YTD.", "Error",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
                catch (System.Exception ex)
                {
                    MessageBox.Show($"Failed to load YTD: {ex.Message}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }

        }

        private void LoadModelBtn_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = "GTA V Models (*.ydr, *.yft)|*.ydr;*.yft|All files (*.*)|*.*",
                Title = "Select a YDR or YFT Model"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                try
                {
                    byte[] data = File.ReadAllBytes(openFileDialog.FileName);
                    if (openFileDialog.FileName.EndsWith(".ydr", System.StringComparison.OrdinalIgnoreCase))
                    {
                        var ydr = new YdrFile();
                        ydr.Load(data);
                        if (ydr.Drawable != null) Viewport3D.LoadDrawable(ydr.Drawable);
                    }
                    else if (openFileDialog.FileName.EndsWith(".yft", System.StringComparison.OrdinalIgnoreCase))
                    {
                        var yft = new YftFile();
                        yft.Load(data);
                        if (yft.Fragment?.Drawable != null) Viewport3D.LoadDrawable(yft.Fragment.Drawable);
                    }
                    else
                    {
                        MessageBox.Show("Could not find a valid Drawable in this YDR.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
                catch (System.Exception ex)
                {
                    MessageBox.Show($"Failed to load YDR: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void ToggleViewportBtn_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "IsToogled")
            {
                Viewport3D.SetActive(ToggleViewportBtn.IsToogled);
            }
        }
    }
}
