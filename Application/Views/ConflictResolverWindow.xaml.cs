using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using ToolKitV.Models;

namespace ToolKitV.Views
{
    // ─── Value converters (file-local, no extra files needed) ────────────────────

    /// <summary>true → Bold, false → Normal</summary>
    public class BoolToFontWeightConverter : IValueConverter
    {
        public static readonly BoolToFontWeightConverter Instance = new();
        public object Convert(object v, Type t, object p, CultureInfo c)
            => (v is true) ? FontWeights.Bold : FontWeights.Normal;
        public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotImplementedException();
    }

    /// <summary>true → Visible, false → Collapsed</summary>
    public class BoolToVisibilityConverter : IValueConverter
    {
        public static readonly BoolToVisibilityConverter Instance = new();
        public object Convert(object v, Type t, object p, CultureInfo c)
            => (v is true) ? Visibility.Visible : Visibility.Collapsed;
        public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotImplementedException();
    }

    // ─── ViewModels ───────────────────────────────────────────────────────────────

    public class ScriptChoiceViewModel : INotifyPropertyChanged
    {
        public string ScriptName     { get; init; } = string.Empty;
        public string GroupName      { get; init; } = string.Empty;   // = CategoryTitle
        public bool   IsRecommended  { get; init; }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; PropertyChanged?.Invoke(this, new(nameof(IsSelected))); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    public class ConflictGroupViewModel
    {
        public string   CategoryTitle  { get; init; } = string.Empty;
        public string   Description    { get; init; } = string.Empty;
        public int      ConflictCount  { get; init; }
        public ObservableCollection<ScriptChoiceViewModel> DetectedScripts { get; init; } = new();
    }

    // ─── Window ───────────────────────────────────────────────────────────────────

    public partial class ConflictResolverWindow : Window
    {
        private readonly List<ConflictGroupViewModel> _groups;

        /// <summary>
        /// Populated when the user clicks "Surgically Resolve".
        /// Key = winner script name, Value = category title.
        /// </summary>
        public Dictionary<string, string> ResolvedChoices { get; private set; } = new();

        /// <param name="detectedConflicts">
        ///   Only categories where ≥2 installed scripts were detected.
        ///   Key = ConflictCategory, Value = installed scripts in that category.
        /// </param>
        /// <param name="installedResources">
        ///   Full set of installed resources for ecosystem-aware pre-selection.
        /// </param>
        public ConflictResolverWindow(
            Dictionary<ConflictCategory, List<string>> detectedConflicts,
            HashSet<string> installedResources)
        {
            InitializeComponent();
            _groups = BuildGroups(detectedConflicts, installedResources);
            icConflicts.ItemsSource = _groups;
        }

        // ── Group builder ────────────────────────────────────────────────────────

        private static List<ConflictGroupViewModel> BuildGroups(
            Dictionary<ConflictCategory, List<string>> conflicts,
            HashSet<string> installed)
        {
            var result = new List<ConflictGroupViewModel>();

            foreach (var (cat, scripts) in conflicts)
            {
                string? preferred = ConflictDefinitions.GetPreferredWinner(cat, installed);

                var choices = new ObservableCollection<ScriptChoiceViewModel>();
                bool anyChecked = false;

                foreach (var script in scripts)
                {
                    bool isRec  = script.Equals(preferred, StringComparison.OrdinalIgnoreCase);
                    var  choice = new ScriptChoiceViewModel
                    {
                        ScriptName    = script,
                        GroupName     = cat.Title,
                        IsRecommended = isRec,
                        IsSelected    = isRec && !anyChecked
                    };
                    if (choice.IsSelected) anyChecked = true;
                    choices.Add(choice);
                }

                // Fallback: select first if nothing was recommended
                if (!anyChecked && choices.Count > 0)
                    choices[0].IsSelected = true;

                result.Add(new ConflictGroupViewModel
                {
                    CategoryTitle  = cat.Title,
                    Description    = cat.Description,
                    ConflictCount  = scripts.Count,
                    DetectedScripts = choices
                });
            }

            return result;
        }

        // ── Handlers ────────────────────────────────────────────────────────────

        private void BtnResolve_Click(object sender, RoutedEventArgs e)
        {
            ResolvedChoices.Clear();

            foreach (var group in _groups)
            {
                foreach (var choice in group.DetectedScripts)
                {
                    if (choice.IsSelected)
                    {
                        ResolvedChoices[choice.ScriptName] = group.CategoryTitle;
                        break;
                    }
                }
            }

            DialogResult = true;
            Close();
        }

        private void BtnIgnore_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void DragBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
            => DragMove();
    }
}
