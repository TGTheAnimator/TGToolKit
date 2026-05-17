using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ToolKitV.Models;
using ToolKitV.ViewModels;

namespace ToolKitV.Views
{
    public partial class SirenBuilder : UserControl
    {
        private const int MaxRows = 20; // GTA V carcols.meta supports up to 20 siren entries

        private readonly ObservableCollection<SirenRow> _rows = new();
        private SirenRow? _selectedRow;

        public SirenBuilder()
        {
            InitializeComponent();
            SirenRowsList.ItemsSource = _rows;

            // Start with 4 default rows
            for (int i = 0; i < 4; i++)
                AddDefaultRow();
        }

        private void AddDefaultRow()
        {
            if (_rows.Count >= MaxRows)
            {
                StatusText.Text = $"Maximum of {MaxRows} sirens reached (GTA V carcols.meta limit).";
                return;
            }

            var row = new SirenRow(_rows.Count + 1);
            _rows.Add(row);
        }

        private void AddRow_Click(object sender, RoutedEventArgs e)
        {
            AddDefaultRow();
            StatusText.Text = $"Added Siren {_rows.Count}. Max: {MaxRows}.";
        }

        private void RemoveRow_Click(object sender, RoutedEventArgs e)
        {
            if (_rows.Count == 0) return;

            if (_selectedRow == _rows[^1])
            {
                _selectedRow.IsSelected = false;
                _selectedRow = null;
            }

            _rows.RemoveAt(_rows.Count - 1);
            StatusText.Text = $"Removed last siren row. {_rows.Count} row(s) remaining.";
        }

        private void RowLabel_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is SirenRow row)
            {
                // Deselect previous row
                if (_selectedRow != null)
                    _selectedRow.IsSelected = false;

                _selectedRow          = row;
                _selectedRow.IsSelected = true;

                StatusText.Text = $"✔ Selected: {row.LightName} — now pick a preset and click 'Apply to Selected'.";
            }
        }

        private void ApplyPreset_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedRow == null)
            {
                StatusText.Text = "Click a row label first to select it, then apply a preset.";
                return;
            }

            bool[]? pattern = PresetCombo.SelectedIndex switch
            {
                0 => SirenGenerator.Alternating,
                1 => SirenGenerator.SlowPulse,
                2 => SirenGenerator.DoubleFlash,
                3 => SirenGenerator.SteadyOn,
                4 => SirenGenerator.RapidBurst,
                _ => null
            };

            if (pattern == null)
            {
                StatusText.Text = "Select a preset from the dropdown first.";
                return;
            }

            _selectedRow.ApplyPattern(pattern);
            StatusText.Text = $"Applied '{(PresetCombo.SelectedItem as ComboBoxItem)?.Content}' to {_selectedRow.LightName}.";
        }

        private void Generate_Click(object sender, RoutedEventArgs e)
        {
            if (_rows.Count == 0)
            {
                XmlOutput.Text = "// Add at least one siren row first.";
                return;
            }

            string xml = SirenGenerator.GenerateCarcols(_rows.ToList());
            XmlOutput.Text = xml;
            StatusText.Text = $"Generated carcols.meta snippet for {_rows.Count} siren(s). Copy and paste into your vehicle's carcols.meta <sirens> block.";
        }

        private void Copy_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(XmlOutput.Text) ||
                XmlOutput.Text.StartsWith("//"))
            {
                StatusText.Text = "Generate XML first, then copy.";
                return;
            }

            Clipboard.SetText(XmlOutput.Text);
            StatusText.Text = "✓ Copied to clipboard.";

            // Flash the button text briefly
            CopyBtn.Content = "✓ Copied!";
            var timer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2)
            };
            timer.Tick += (_, _) =>
            {
                CopyBtn.Content = "Copy";
                timer.Stop();
            };
            timer.Start();
        }

        // ─── Per-row shift / invert handlers ─────────────────────────────────────

        private void ShiftLeft_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is SirenRow row)
            {
                row.ShiftLeft();
                StatusText.Text = $"{row.LightName}: pattern shifted ◄ left.";
            }
        }

        private void ShiftRight_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is SirenRow row)
            {
                row.ShiftRight();
                StatusText.Text = $"{row.LightName}: pattern shifted ► right.";
            }
        }

        private void Invert_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is SirenRow row)
            {
                row.InvertPattern();
                StatusText.Text = $"{row.LightName}: pattern inverted.";
            }
        }
    }
}
