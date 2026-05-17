using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ToolKitV.ViewModels
{
    /// <summary>
    /// Wraps a single boolean tick (one 32nd of a GTA V siren sequence).
    /// Must be a class (not struct) so WPF TwoWay bindings work correctly.
    /// </summary>
    public class TickCell : INotifyPropertyChanged
    {
        private bool _isOn;

        public bool IsOn
        {
            get => _isOn;
            set
            {
                if (_isOn == value) return;
                _isOn = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    /// <summary>
    /// Represents one siren light row with 32 TickCells.
    /// Exposes GetSequenceInteger() so the UI can display the bitmask value live.
    /// </summary>
    public class SirenRow : INotifyPropertyChanged
    {
        private string _lightName;

        public int    LightId   { get; }
        public string LightName
        {
            get => _lightName;
            set
            {
                if (_lightName == value) return;
                _lightName = value;
                OnPropertyChanged();
            }
        }

        private bool _isSelected;
        /// <summary>
        /// True when this row is the active selection for preset application.
        /// Drives LabelForeground so the user always knows which siren is targeted.
        /// </summary>
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value) return;
                _isSelected = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(LabelForeground));
            }
        }

        /// <summary>Accent red when selected, dim white when idle.</summary>
        public string LabelForeground => _isSelected ? "#FF5555" : "#C0FFFFFF";

        public ObservableCollection<TickCell> Ticks { get; }

        /// <summary>
        /// The 32-bit integer representation of this row's sequence,
        /// updated live as the user toggles ticks.
        /// </summary>
        public uint SequenceDisplay => GetSequenceInteger();

        public SirenRow(int id)
        {
            LightId    = id;
            _lightName = $"Siren {id}";

            Ticks = new ObservableCollection<TickCell>();
            for (int i = 0; i < 32; i++)
            {
                var cell = new TickCell();
                // Propagate tick change to row-level SequenceDisplay property
                cell.PropertyChanged += (_, _) => OnPropertyChanged(nameof(SequenceDisplay));
                Ticks.Add(cell);
            }
        }

        public uint GetSequenceInteger()
        {
            uint result = 0;
            for (int i = 0; i < 32; i++)
            {
                if (Ticks[i].IsOn)
                    result |= 1u << (31 - i);
            }
            return result;
        }

        public void ApplyPattern(bool[] pattern)
        {
            for (int i = 0; i < 32 && i < pattern.Length; i++)
                Ticks[i].IsOn = pattern[i];
        }

        // ─── Data manipulation commands ───────────────────────────────────────────

        /// <summary>Rotates the entire pattern one step to the right (circular shift).</summary>
        public void ShiftRight()
        {
            bool last = Ticks[31].IsOn;
            for (int i = 31; i > 0; i--)
                Ticks[i].IsOn = Ticks[i - 1].IsOn;
            Ticks[0].IsOn = last;
            OnPropertyChanged(nameof(SequenceDisplay));
        }

        /// <summary>Rotates the entire pattern one step to the left (circular shift).</summary>
        public void ShiftLeft()
        {
            bool first = Ticks[0].IsOn;
            for (int i = 0; i < 31; i++)
                Ticks[i].IsOn = Ticks[i + 1].IsOn;
            Ticks[31].IsOn = first;
            OnPropertyChanged(nameof(SequenceDisplay));
        }

        /// <summary>Flips every tick — great for building alternating lightbar sequences.</summary>
        public void InvertPattern()
        {
            for (int i = 0; i < 32; i++)
                Ticks[i].IsOn = !Ticks[i].IsOn;
            OnPropertyChanged(nameof(SequenceDisplay));
        }

        /// <summary>
        /// Applies a named preset, clearing the grid first.
        /// Supported: "QuadFlash", "DoubleFlash", "Alternating", "SteadyOn"
        /// </summary>
        public void ApplyPreset(string patternType)
        {
            // Clear all
            for (int i = 0; i < 32; i++) Ticks[i].IsOn = false;

            switch (patternType)
            {
                case "QuadFlash":
                    // 1010 1010 0000 0000 …
                    Ticks[0].IsOn = Ticks[2].IsOn = Ticks[4].IsOn = Ticks[6].IsOn = true;
                    break;
                case "DoubleFlash":
                    Ticks[0].IsOn = Ticks[2].IsOn = true;
                    break;
                case "Alternating":
                    for (int i = 0; i < 32; i += 2) Ticks[i].IsOn = true;
                    break;
                case "SteadyOn":
                    for (int i = 0; i < 32; i++) Ticks[i].IsOn = true;
                    break;
            }

            OnPropertyChanged(nameof(SequenceDisplay));
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
