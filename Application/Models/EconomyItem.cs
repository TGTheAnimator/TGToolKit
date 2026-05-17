using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ToolKitV.Models
{
    public class EconomyItem : INotifyPropertyChanged
    {
        private string _spawnCode = string.Empty;
        private string _label = string.Empty;
        private float _weight;
        private int _buyPrice;
        private int _sellPrice;
        private float _dropChance;
        private string _definitionFilePath = string.Empty;
        private string _shopFilePath = string.Empty;
        private bool _isModified;

        public string SpawnCode
        {
            get => _spawnCode;
            set { _spawnCode = value; OnPropertyChanged(); }
        }

        public string Label
        {
            get => _label;
            set { _label = value; OnPropertyChanged(); }
        }

        public float Weight
        {
            get => _weight;
            set { _weight = value; OnPropertyChanged(); IsModified = true; }
        }

        public int BuyPrice
        {
            get => _buyPrice;
            set { _buyPrice = value; OnPropertyChanged(); IsModified = true; }
        }

        public int SellPrice
        {
            get => _sellPrice;
            set { _sellPrice = value; OnPropertyChanged(); IsModified = true; }
        }

        public float DropChance
        {
            get => _dropChance;
            set { _dropChance = value; OnPropertyChanged(); IsModified = true; }
        }

        public string DefinitionFilePath
        {
            get => _definitionFilePath;
            set { _definitionFilePath = value; OnPropertyChanged(); }
        }

        public string ShopFilePath
        {
            get => _shopFilePath;
            set { _shopFilePath = value; OnPropertyChanged(); }
        }

        public bool IsModified
        {
            get => _isModified;
            set 
            { 
                _isModified = value; 
                OnPropertyChanged(); 
                OnPropertyChanged(nameof(RowColor)); 
                OnPropertyChanged(nameof(StatusText)); 
            }
        }

        // UI Helpers
        public string RowColor => IsModified ? "#FFA500" : "#CCCCCC";
        public string StatusText => IsModified ? "Pending Sync" : "Synchronized";

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
