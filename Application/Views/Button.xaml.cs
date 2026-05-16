using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ToolKitV.Views
{
    public partial class Button : UserControl, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        public event RoutedEventHandler? Click;

        private void NotifyPropertyChanged([CallerMemberName] string propertyName = "")
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        private string _title = "";
        public string Title
        {
            get => _title;
            set { if (value != _title) { _title = value; NotifyPropertyChanged(); } }
        }

        private bool _isEnabled = true;
        public bool IsButtonEnabled
        {
            get => _isEnabled;
            set { if (value != _isEnabled) { _isEnabled = value; NotifyPropertyChanged(); } }
        }

        // Legacy compat — initial value setter used from XAML attribute.
        public bool IsButtonEnabledValue
        {
            get => _isEnabled;
            set => IsButtonEnabled = value;
        }

        public Button()
        {
            InitializeComponent();
            DataContext = this;
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            if (IsButtonEnabled)
                Click?.Invoke(sender, e);
        }

        /// <summary>
        /// Sets the progress fill width as a fraction [0-100] of the button's actual width.
        /// Call from the UI thread only (use Dispatcher.Invoke if needed).
        /// </summary>
        public void SetProgress(double percent)
        {
            // Walk the visual tree to find the named Progress border.
            var progress = FindVisualChild<Border>("Progress");
            if (progress is null) return;

            double total = ActualWidth > 0 ? ActualWidth : 180;
            progress.Width = System.Math.Ceiling(total / 100.0 * percent);
        }

        public void ResetProgress()
        {
            var progress = FindVisualChild<Border>("Progress");
            if (progress is not null) progress.Width = 0;
        }

        private T? FindVisualChild<T>(string name) where T : FrameworkElement
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(this); i++)
            {
                var child = VisualTreeHelper.GetChild(this, i);
                if (child is T t && t.Name == name) return t;
                if (child is FrameworkElement fe)
                {
                    var result = FindVisualChildRecursive<T>(fe, name);
                    if (result is not null) return result;
                }
            }
            return null;
        }

        private static T? FindVisualChildRecursive<T>(DependencyObject parent, string name) where T : FrameworkElement
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T t && t.Name == name) return t;
                var result = FindVisualChildRecursive<T>(child, name);
                if (result is not null) return result;
            }
            return null;
        }
    }
}
