using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
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

    /// <summary>
    /// Multi-binding: Visible when values[0]=true (IsDefault) AND values[1]=false (IsRecommended).
    /// Shows the FRAMEWORK DEFAULT badge only on non-recommended defaults.
    /// </summary>
    public class DefaultBadgeVisibilityConverter : IMultiValueConverter
    {
        public static readonly DefaultBadgeVisibilityConverter Instance = new();
        public object Convert(object[] values, Type t, object p, CultureInfo c)
        {
            bool isDefault     = values.Length > 0 && values[0] is true;
            bool isRecommended = values.Length > 1 && values[1] is true;
            return (isDefault && !isRecommended) ? Visibility.Visible : Visibility.Collapsed;
        }
        public object[] ConvertBack(object v, Type[] t, object p, CultureInfo c) => throw new NotImplementedException();
    }

    // ─── ViewModels ───────────────────────────────────────────────────────────────

    public class ScriptChoiceViewModel : INotifyPropertyChanged
    {
        public string ScriptName     { get; init; } = string.Empty;
        public string GroupName      { get; init; } = string.Empty;
        public bool   IsRecommended  { get; init; }
        /// <summary>True when this script is a known QBCore/Qbox framework default.</summary>
        public bool   IsDefault      { get; init; }

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
                // Determine the recommended winner (premium-first, never a framework default)
                string? preferred = ConflictDefinitions.GetPreferredWinner(cat, installed);

                var choices = new ObservableCollection<ScriptChoiceViewModel>();

                foreach (var script in scripts)
                {
                    bool isDefault = ConflictDefinitions.FrameworkDefaultScripts.Contains(script);
                    bool isRec     = script.Equals(preferred, StringComparison.OrdinalIgnoreCase);

                    choices.Add(new ScriptChoiceViewModel
                    {
                        ScriptName    = script,
                        GroupName     = cat.Title,
                        IsRecommended = isRec,
                        IsDefault     = isDefault,
                        IsSelected    = false   // will be set by PreSelectWinner
                    });
                }

                PreSelectWinner(choices, preferred);

                result.Add(new ConflictGroupViewModel
                {
                    CategoryTitle   = cat.Title,
                    Description     = cat.Description,
                    ConflictCount   = scripts.Count,
                    DetectedScripts = choices
                });
            }

            return result;
        }

        /// <summary>
        /// Selects the best winner in a group:
        /// 1. Explicitly preferred script (from ecosystem detection)
        /// 2. First non-framework-default script
        /// 3. First script (fallback)
        /// </summary>
        private static void PreSelectWinner(
            ObservableCollection<ScriptChoiceViewModel> choices,
            string? preferred)
        {
            foreach (var c in choices) c.IsSelected = false;

            // Pass 1: explicit ecosystem recommendation
            if (preferred != null)
            {
                var rec = choices.FirstOrDefault(
                    c => c.ScriptName.Equals(preferred, StringComparison.OrdinalIgnoreCase));
                if (rec != null) { rec.IsSelected = true; return; }
            }

            // Pass 2: any premium (non-default) script
            var premium = choices.FirstOrDefault(c => !c.IsDefault);
            if (premium != null) { premium.IsSelected = true; return; }

            // Pass 3: fallback to first
            if (choices.Count > 0) choices[0].IsSelected = true;
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
