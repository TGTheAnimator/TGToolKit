using System.Windows;
using System.Windows.Input;

namespace ToolKitV.Views
{
    public partial class PasswordPromptWindow : Window
    {
        public string Password { get; private set; } = string.Empty;

        public PasswordPromptWindow(string title, string message)
        {
            InitializeComponent();
            TitleLabel.Text = title;
            MessageLabel.Text = message;
            
            Loaded += (s, e) =>
            {
                PromptPassword.Focus();
            };
        }

        private void DragBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
                DragMove();
        }

        private void BtnConfirm_Click(object sender, RoutedEventArgs e)
        {
            Password = PromptPassword.Password;
            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void PromptPassword_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Return || e.Key == Key.Enter)
            {
                BtnConfirm_Click(this, new RoutedEventArgs());
            }
        }
    }
}
