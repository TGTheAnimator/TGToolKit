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

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
