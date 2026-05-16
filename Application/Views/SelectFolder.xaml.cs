using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace ToolKitV.Views
{
    public partial class SelectFolder : UserControl, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private void NotifyPropertyChanged([CallerMemberName] string propertyName = "")
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        public string Title { get; set; } = "";

        private string _path = "";
        public string Path
        {
            get => _path;
            set
            {
                if (value != _path)
                {
                    _path = value;
                    NotifyPropertyChanged();
                }
            }
        }

        public SelectFolder()
        {
            InitializeComponent();
            DataContext = this;
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            // OpenFolderDialog is available in .NET 8 WPF without WinForms.
            OpenFolderDialog dialog = new()
            {
                Title       = "Select folder",
                Multiselect = false,
            };

            if (dialog.ShowDialog() == true)
            {
                Path = dialog.FolderName;
            }
        }
    }
}
