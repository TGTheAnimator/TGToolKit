using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Controls;

namespace ToolKitV.Views
{
    public partial class InputText : UserControl, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private void NotifyPropertyChanged([CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public string Title { get; set; } = "";
        public string Description { get; set; } = "";

        public bool IsInputEnabledValue { get; set; } = true;
        public bool IsInputEnabled
        {
            get => IsInputEnabledValue;
            set
            {
                if (value != IsInputEnabledValue)
                {
                    IsInputEnabledValue = value;
                    NotifyPropertyChanged();
                }
            }
        }

        private string _textValue = "";
        public string TextValue
        {
            get => _textValue;
            set
            {
                if (value != _textValue)
                {
                    _textValue = value;
                    NotifyPropertyChanged();
                }
            }
        }

        public InputText()
        {
            InitializeComponent();
            DataContext = this;
        }
    }
}
